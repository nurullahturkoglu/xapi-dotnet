using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Xapi.Json;

namespace Xapi.Lrs.Http;

internal static class RequestReading
{
    // IEEE 9274.1.1, 4.1.5: reject parameters the LRS does not recognize, and known parameters spelled in
    // another case. ASP.NET Core merges query keys case-insensitively, so a name in two spellings shows up as
    // one key with two values.
    public static string? CheckParameters(IQueryCollection query, IReadOnlyCollection<string> allowed)
    {
        foreach (var (name, values) in query)
        {
            if (!allowed.Contains(name))
            {
                var match = allowed.FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
                return match is null
                    ? $"The parameter '{name}' is not recognized here."
                    : $"The parameter '{name}' must be spelled '{match}'.";
            }

            if (values.Count != 1)
            {
                return $"The parameter '{name}' must appear exactly once.";
            }
        }

        return null;
    }

    // The agent parameter is an Agent, not a Group (1.0.3 Communication 2.4 and 2.6, note; IEEE 9274.1.1 tables
    // 4.1.6.2, 4.1.6.3 and 4.1.6.5). A Group's identifier can be sent inside an Agent object instead.
    public static bool TryReadAgent(IQueryCollection query, XapiVersion version, [NotNullWhen(true)] out Agent? agent, out IResult error)
    {
        agent = null;
        error = default!;
        if (!query.TryGetValue("agent", out var value))
        {
            error = XapiResults.Problem(StatusCodes.Status400BadRequest, "The agent parameter is required.");
            return false;
        }

        var parsed = XapiJson.ParseActor(value.ToString(), version);
        if (!parsed.IsSuccess)
        {
            error = XapiResults.Problem(StatusCodes.Status400BadRequest, "The agent parameter is not a valid Agent.", parsed.Errors);
            return false;
        }

        if (parsed.Value is not Agent found)
        {
            error = XapiResults.Problem(StatusCodes.Status400BadRequest, "The agent parameter must be an Agent, not a Group. Send the Group's identifier in an Agent object.");
            return false;
        }

        agent = found;
        return true;
    }

    // UUIDs must be in the standard string form (4.2.7)
    public static bool TryParseUuid(string? value, out Guid uuid) => Guid.TryParseExact(value, "D", out uuid);

    public static bool IsMediaType(HttpRequest request, string mediaType) =>
        MediaTypeHeaderValue.TryParse(request.ContentType, out var parsed) &&
        parsed.MediaType.Equals(mediaType, StringComparison.OrdinalIgnoreCase);

    public static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
