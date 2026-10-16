using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Xapi.Json;
using Xapi.Lrs.Security;
using Xapi.Lrs.Statements;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Http;

// The Statement Resource (IEEE 9274.1.1, 4.1.6.1).
internal static class StatementEndpoints
{
    private static readonly string[] PutParameters = ["statementId"];

    private static readonly string[] GetParameters =
    [
        "statementId", "voidedStatementId", "agent", "verb", "activity", "registration", "related_activities",
        "related_agents", "since", "until", "limit", "format", "attachments", "ascending",
    ];

    private static readonly string[] SingleStatementParameters = ["statementId", "voidedStatementId", "format", "attachments"];

    public static async Task<IResult> PutAsync(HttpContext http, StatementWriter writer)
    {
        var request = XapiRequestFeature.Get(http);
        var query = http.Request.Query;
        if (RequestReading.CheckParameters(query, PutParameters) is { } parameterError)
        {
            return BadRequest(parameterError);
        }

        if (!query.TryGetValue("statementId", out var idValue))
        {
            return BadRequest("The statementId parameter is required.");
        }

        if (!RequestReading.TryParseUuid(idValue, out var statementId))
        {
            return BadRequest($"statementId '{idValue}' is not a UUID.");
        }

        if (CheckContentType(http.Request) is { } contentError)
        {
            return contentError;
        }

        var body = await RequestReading.ReadBodyAsync(http.Request, http.RequestAborted).ConfigureAwait(false);
        var parsed = XapiJson.ParseStatement(body, request.Version);
        if (!parsed.IsSuccess)
        {
            return XapiResults.Problem(StatusCodes.Status400BadRequest, "The statement is not valid.", parsed.Errors);
        }

        var statement = parsed.Value;
        if (statement.Id is { } id && id != statementId)
        {
            return BadRequest($"The statement id {id} does not match the statementId parameter {statementId}.");
        }

        var (result, _) = await writer.WriteAsync([statement with { Id = statementId }], request.Version, request.Caller, http.RequestAborted).ConfigureAwait(false);
        return result.IsConflict
            ? Conflict(result.ConflictingStatementId.Value)
            : XapiResults.Status(StatusCodes.Status204NoContent);
    }

    public static async Task<IResult> PostAsync(HttpContext http, StatementWriter writer)
    {
        var request = XapiRequestFeature.Get(http);
        if (http.Request.Query.Count > 0)
        {
            // xAPI 1.0.3 Alternate Request Syntax (?method=...) is not implemented, so every parameter is unknown
            return BadRequest(RequestReading.CheckParameters(http.Request.Query, []) ?? "No parameters are allowed.");
        }

        if (CheckContentType(http.Request) is { } contentError)
        {
            return contentError;
        }

        var body = await RequestReading.ReadBodyAsync(http.Request, http.RequestAborted).ConfigureAwait(false);
        var parsed = XapiJson.ParseStatements(body, request.Version);
        if (!parsed.IsSuccess)
        {
            var duplicate = parsed.Errors.Any(e => e.Code == XapiErrorCodes.BatchDuplicateId);
            return XapiResults.Problem(
                StatusCodes.Status400BadRequest,
                duplicate ? "The batch contains the same statement id more than once." : "The statements are not valid.",
                parsed.Errors);
        }

        var (result, ids) = await writer.WriteAsync(parsed.Value, request.Version, request.Caller, http.RequestAborted).ConfigureAwait(false);
        return result.IsConflict
            ? Conflict(result.ConflictingStatementId.Value)
            : XapiResults.Json(WriteIds(ids));
    }

    public static async Task<IResult> GetAsync(HttpContext http, IStatementStore store)
    {
        var request = XapiRequestFeature.Get(http);
        var query = http.Request.Query;
        if (RequestReading.CheckParameters(query, GetParameters) is { } parameterError)
        {
            return BadRequest(parameterError);
        }

        var hasStatementId = query.TryGetValue("statementId", out var statementIdValue);
        var hasVoidedId = query.TryGetValue("voidedStatementId", out var voidedIdValue);
        if (hasStatementId && hasVoidedId)
        {
            return BadRequest("Use either statementId or voidedStatementId, not both.");
        }

        if (!hasStatementId && !hasVoidedId)
        {
            return XapiResults.Problem(StatusCodes.Status501NotImplemented, "Statement queries are not implemented yet; fetch single statements with statementId or voidedStatementId.");
        }

        if (query.Keys.FirstOrDefault(k => !SingleStatementParameters.Contains(k)) is { } extra)
        {
            return BadRequest($"The parameter '{extra}' cannot be combined with {(hasStatementId ? "statementId" : "voidedStatementId")}.");
        }

        var idValue = hasStatementId ? statementIdValue : voidedIdValue;
        if (!RequestReading.TryParseUuid(idValue, out var id))
        {
            return BadRequest($"'{idValue}' is not a UUID.");
        }

        if (!TryReadFormat(query, out var format) || !TryReadBoolean(query, "attachments", out var attachments))
        {
            return BadRequest("format must be exact, ids or canonical, and attachments must be true or false.");
        }

        var stored = await store.GetAsync(id, http.RequestAborted).ConfigureAwait(false);

        // IEEE 9274.1.1, 4.1.6.1 "Voided Statements": a voided statement is only returned by voidedStatementId,
        // and voidedStatementId only returns voided statements
        if (stored is null || stored.IsVoided != hasVoidedId || !MayRead(request.Caller, stored))
        {
            return XapiResults.Problem(StatusCodes.Status404NotFound, $"No {(hasVoidedId ? "voided " : "")}statement with id {id} was found.");
        }

        var json = format == StatementFormat.Exact ? stored.Json : Reformat(stored, format, AcceptLanguages(http.Request));
        if (!attachments)
        {
            return XapiResults.Json(json, stored.Stored);
        }

        // attachments=true asks for the multipart format. Attachment data is not stored yet (multipart requests are
        // rejected), so the statement part is the only part.
        var boundary = "xapi-" + Guid.NewGuid().ToString("N");
        var multipart = $"--{boundary}\r\nContent-Type: application/json\r\n\r\n{json}\r\n--{boundary}--\r\n";
        return XapiResults.Body($"multipart/mixed; boundary={boundary}", Encoding.UTF8.GetBytes(multipart), stored.Stored);
    }

    private static IResult? CheckContentType(HttpRequest request)
    {
        if (RequestReading.IsMediaType(request, "application/json"))
        {
            return null;
        }

        return RequestReading.IsMediaType(request, "multipart/mixed")
            ? BadRequest("multipart/mixed statement requests (attachments sent with the statement) are not supported yet; send application/json with fileUrl attachments.")
            : BadRequest("Statements must be sent as application/json.");
    }

    private static bool MayRead(XapiCaller caller, StoredStatement statement) =>
        Scopes.CanReadAllStatements(caller.Scopes) ||
        string.Equals(statement.AuthorityKey, caller.AuthorityKey, StringComparison.Ordinal);

    private static string Reformat(StoredStatement stored, StatementFormat format, IReadOnlyList<string> languages)
    {
        var parsed = XapiJson.ParseStatement(stored.Json, XapiVersion.V2_0_0, XapiReadMode.Lenient);
        var statement = parsed.IsSuccess
            ? parsed.Value
            : throw new InvalidOperationException("A stored statement could not be read: " + string.Join("; ", parsed.Errors));
        var version = stored.Version.StartsWith("2.", StringComparison.Ordinal) ? XapiVersion.V2_0_0 : XapiVersion.V1_0_3;
        return XapiJson.SerializeStatement(statement, version, format, languages);
    }

    private static List<string> AcceptLanguages(HttpRequest request) =>
        [.. request.GetTypedHeaders().AcceptLanguage
            .Where(l => l.Quality is not 0)
            .OrderByDescending(l => l.Quality ?? 1)
            .Select(l => l.Value.ToString())];

    private static bool TryReadFormat(IQueryCollection query, out StatementFormat format)
    {
        format = StatementFormat.Exact;
        if (!query.TryGetValue("format", out var value))
        {
            return true;
        }

        switch (value.ToString())
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
                return false;
        }
    }

    private static bool TryReadBoolean(IQueryCollection query, string name, out bool value)
    {
        value = false;
        if (!query.TryGetValue(name, out var text))
        {
            return true;
        }

        // Boolean parameters are spelled as in JSON (4.1.6.1 GET, note)
        switch (text.ToString())
        {
            case "true":
                value = true;
                return true;
            case "false":
                return true;
            default:
                return false;
        }
    }

    private static string WriteIds(IReadOnlyList<Guid> ids)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var id in ids)
            {
                writer.WriteStringValue(id.ToString("D"));
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static IResult BadRequest(string detail) => XapiResults.Problem(StatusCodes.Status400BadRequest, detail);

    private static IResult Conflict(Guid id) =>
        XapiResults.Problem(StatusCodes.Status409Conflict, $"A different statement with id {id} is already stored. Stored statements cannot be changed.");
}
