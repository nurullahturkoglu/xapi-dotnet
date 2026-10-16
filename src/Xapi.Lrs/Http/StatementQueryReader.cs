using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Xapi.Json;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Http;

// A statement query parsed from its parameters (IEEE 9274.1.1, 4.1.6.1 GET).
internal sealed record StatementQueryRequest(StatementFilter Filter, StatementFormat Format, bool Attachments, IReadOnlyDictionary<string, string> Parameters);

// Reads the query parameters of GET /statements, and writes and reads the "more" value of a StatementResult.
//
// "more" is <path>/statements?more=<token>. The token is the base64url-encoded query string of the query with
// the page size it ran with and the storage key of the last statement returned (ADR-020). The next page runs
// the same filters below (or above, when ascending) that key, so pages stay stable when statements are stored
// in between, without state on the server. The token is opaque to clients but not secret or signed: it can
// only express a query the caller could send anyway, and scope restrictions come from the request's
// credentials, not from the token.
internal static class StatementQueryReader
{
    public const string MoreParameter = "more";

    // Only inside "more" tokens
    private const string AfterParameter = "after";

    private static readonly HashSet<string> TokenParameters = new(StringComparer.Ordinal)
    {
        "agent", "verb", "activity", "registration", "related_activities", "related_agents", "since", "until", "limit",
        "format", "attachments", "ascending", AfterParameter,
    };

    public static bool TryRead(
        IReadOnlyDictionary<string, string> parameters,
        XapiVersion version,
        int maxLimit,
        [NotNullWhen(true)] out StatementQueryRequest? request,
        [NotNullWhen(false)] out string? error)
    {
        request = null;
        var filter = new StatementFilter { Limit = maxLimit };
        if (parameters.TryGetValue("agent", out var agentJson))
        {
            var agent = XapiJson.ParseActor(agentJson, version);
            if (!agent.IsSuccess || agent.Value is Group { Ifi: null })
            {
                error = "agent must be an Agent or an identified Group, as JSON.";
                return false;
            }

            filter = filter with { Agent = agent.Value };
        }

        if (parameters.TryGetValue("verb", out var verbText))
        {
            if (!Iri.TryParse(verbText, out var verb))
            {
                error = $"verb '{verbText}' is not an IRI.";
                return false;
            }

            filter = filter with { Verb = verb };
        }

        if (parameters.TryGetValue("activity", out var activityText))
        {
            if (!Iri.TryParse(activityText, out var activity))
            {
                error = $"activity '{activityText}' is not an IRI.";
                return false;
            }

            filter = filter with { Activity = activity };
        }

        if (parameters.TryGetValue("registration", out var registrationText))
        {
            if (!RequestReading.TryParseUuid(registrationText, out var registration))
            {
                error = $"registration '{registrationText}' is not a UUID.";
                return false;
            }

            filter = filter with { Registration = registration };
        }

        if (!TryReadTimestamp(parameters, "since", version, out var since, out error) ||
            !TryReadTimestamp(parameters, "until", version, out var until, out error) ||
            !TryReadBoolean(parameters, "related_activities", out var relatedActivities, out error) ||
            !TryReadBoolean(parameters, "related_agents", out var relatedAgents, out error) ||
            !TryReadBoolean(parameters, "ascending", out var ascending, out error) ||
            !TryReadBoolean(parameters, "attachments", out var attachments, out error) ||
            !TryReadFormat(parameters, out var format, out error) ||
            !TryReadLimit(parameters, maxLimit, out var limit, out error))
        {
            return false;
        }

        filter = filter with
        {
            Since = since,
            Until = until,
            RelatedActivities = relatedActivities,
            RelatedAgents = relatedAgents,
            Ascending = ascending,
            Limit = limit,
        };

        if (parameters.TryGetValue(AfterParameter, out var afterText))
        {
            if (!RequestReading.TryParseUuid(afterText, out var after))
            {
                error = "The more value is not one this LRS returned.";
                return false;
            }

            filter = filter with { After = after };
        }

        request = new StatementQueryRequest(filter, format, attachments, parameters);
        error = null;
        return true;
    }

    // The relative IRL of the next page: the request's own path (path base included), so it resolves against
    // whatever URL the client used to reach the LRS.
    public static string MoreUrl(HttpRequest httpRequest, StatementQueryRequest query, Guid continuation)
    {
        var pairs = query.Parameters
            .Where(p => p.Key is not ("limit" or AfterParameter))
            .Append(new("limit", query.Filter.Limit.ToString(CultureInfo.InvariantCulture)))
            .Append(new(AfterParameter, continuation.ToString("D")));
        var text = QueryString.Create(pairs!).Value![1..];
        return $"{httpRequest.PathBase}{httpRequest.Path}?{MoreParameter}={Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text))}";
    }

    public static bool TryReadToken(string token, [NotNullWhen(true)] out Dictionary<string, string>? parameters)
    {
        parameters = null;
        string text;
        try
        {
            text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
        }
        catch (FormatException)
        {
            return false;
        }

        var parsed = QueryHelpers.ParseQuery(text);
        if (!parsed.ContainsKey(AfterParameter) || parsed.Any(p => !TokenParameters.Contains(p.Key) || p.Value.Count != 1))
        {
            return false;
        }

        parameters = parsed.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal);
        return true;
    }

    public static bool TryReadFormat(IReadOnlyDictionary<string, string> parameters, out StatementFormat format, [NotNullWhen(false)] out string? error)
    {
        format = StatementFormat.Exact;
        error = null;
        if (!parameters.TryGetValue("format", out var value))
        {
            return true;
        }

        // Enumerated values match exactly, case included (4.1.6, "Requests")
        switch (value)
        {
            case "exact":
                return true;
            case "ids":
                format = StatementFormat.Ids;
                return true;
            case "canonical":
                format = StatementFormat.Canonical;
                return true;
            default:
                error = $"format '{value}' must be exact, ids or canonical.";
                return false;
        }
    }

    public static bool TryReadBoolean(IReadOnlyDictionary<string, string> parameters, string name, out bool value, [NotNullWhen(false)] out string? error)
    {
        value = false;
        error = null;
        if (!parameters.TryGetValue(name, out var text))
        {
            return true;
        }

        // Boolean parameters are spelled as in JSON (4.1.6.1 GET, note)
        switch (text)
        {
            case "true":
                value = true;
                return true;
            case "false":
                return true;
            default:
                error = $"{name} '{text}' must be true or false.";
                return false;
        }
    }

    private static bool TryReadTimestamp(IReadOnlyDictionary<string, string> parameters, string name, XapiVersion version, out DateTimeOffset? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        error = null;
        if (!parameters.TryGetValue(name, out var text))
        {
            return true;
        }

        if (!XapiTimestamp.TryParse(text, version, out var timestamp))
        {
            error = $"{name} '{text}' is not a timestamp.";
            return false;
        }

        value = timestamp;
        return true;
    }

    // "limit": a nonnegative integer; 0 means the server maximum (4.1.6.1 GET). Larger values are lowered to it.
    private static bool TryReadLimit(IReadOnlyDictionary<string, string> parameters, int maxLimit, out int limit, [NotNullWhen(false)] out string? error)
    {
        limit = maxLimit;
        error = null;
        if (!parameters.TryGetValue("limit", out var text))
        {
            return true;
        }

        if (text.Length == 0 || !text.All(char.IsAsciiDigit))
        {
            error = $"limit '{text}' must be a nonnegative integer.";
            return false;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var requested) && requested is > 0 && requested < maxLimit)
        {
            limit = requested;
        }

        return true;
    }
}
