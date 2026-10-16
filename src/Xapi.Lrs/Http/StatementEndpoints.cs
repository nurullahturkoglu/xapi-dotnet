using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
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
        "related_agents", "since", "until", "limit", "format", "attachments", "ascending", StatementQueryReader.MoreParameter,
    ];

    private static readonly string[] SingleStatementParameters = ["statementId", "voidedStatementId", "format", "attachments"];

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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

    public static async Task<IResult> GetAsync(HttpContext http, IStatementStore store, IOptions<XapiLrsOptions> options)
    {
        var request = XapiRequestFeature.Get(http);
        var query = http.Request.Query;
        if (RequestReading.CheckParameters(query, GetParameters) is { } parameterError)
        {
            return BadRequest(parameterError);
        }

        var parameters = query.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal);
        if (parameters.TryGetValue(StatementQueryReader.MoreParameter, out var token))
        {
            if (parameters.Count > 1)
            {
                return BadRequest("The more parameter cannot be combined with other parameters; use the more value as returned.");
            }

            if (!StatementQueryReader.TryReadToken(token, out parameters))
            {
                return BadRequest("The more value is not one this LRS returned.");
            }

            return await QueryAsync(http, store, options.Value, parameters).ConfigureAwait(false);
        }

        var hasStatementId = parameters.TryGetValue("statementId", out var statementIdValue);
        var hasVoidedId = parameters.TryGetValue("voidedStatementId", out var voidedIdValue);
        if (hasStatementId && hasVoidedId)
        {
            return BadRequest("Use either statementId or voidedStatementId, not both.");
        }

        if (!hasStatementId && !hasVoidedId)
        {
            return await QueryAsync(http, store, options.Value, parameters).ConfigureAwait(false);
        }

        // IEEE 9274.1.1, 4.1.6.1 GET: only attachments and format may come with statementId or voidedStatementId
        if (parameters.Keys.FirstOrDefault(k => !SingleStatementParameters.Contains(k)) is { } extra)
        {
            return BadRequest($"The parameter '{extra}' cannot be combined with {(hasStatementId ? "statementId" : "voidedStatementId")}.");
        }

        var idValue = hasStatementId ? statementIdValue : voidedIdValue;
        if (!RequestReading.TryParseUuid(idValue, out var id))
        {
            return BadRequest($"'{idValue}' is not a UUID.");
        }

        if (!StatementQueryReader.TryReadFormat(parameters, out var format, out var error) ||
            !StatementQueryReader.TryReadBoolean(parameters, "attachments", out var attachments, out error))
        {
            return BadRequest(error);
        }

        var stored = await store.GetAsync(id, http.RequestAborted).ConfigureAwait(false);

        // 4.1.6.1 "Voided Statements": a voided statement is only returned by voidedStatementId, and
        // voidedStatementId only returns voided statements
        if (stored is null || stored.IsVoided != hasVoidedId || !MayRead(request.Caller, stored))
        {
            return XapiResults.Problem(StatusCodes.Status404NotFound, $"No {(hasVoidedId ? "voided " : "")}statement with id {id} was found.");
        }

        var rendered = await StatementRendering.RenderAsync([stored], format, AcceptLanguages(http.Request), store, http.RequestAborted).ConfigureAwait(false);
        return JsonOrMultipart(rendered[0], attachments, stored.Stored);
    }

    private static async Task<IResult> QueryAsync(HttpContext http, IStatementStore store, XapiLrsOptions options, Dictionary<string, string> parameters)
    {
        var request = XapiRequestFeature.Get(http);
        if (!StatementQueryReader.TryRead(parameters, request.Version, options.MaxStatementsPerPage, out var query, out var error))
        {
            return BadRequest(error);
        }

        var filter = query.Filter;
        if (!Scopes.CanReadAllStatements(request.Caller.Scopes))
        {
            // statements/read/mine: only the statements stored with the caller's credentials (4.1.6.1 GET: the
            // LRS may apply additional filter criteria based on permissions)
            filter = filter with { AuthorityKey = request.Caller.AuthorityKey };
        }

        var page = await store.QueryAsync(filter, http.RequestAborted).ConfigureAwait(false);
        var statements = await StatementRendering.RenderAsync(page.Statements, query.Format, AcceptLanguages(http.Request), store, http.RequestAborted).ConfigureAwait(false);

        // 1.0.3 (Data 2.5) has "more" as an empty string on the last page; 2.0 allows that too
        var more = page.Continuation is { } continuation ? StatementQueryReader.MoreUrl(http.Request, query, continuation) : "";

        // 4.1.6.1 GET: Last-Modified is the most recent stored time of the returned statements
        DateTimeOffset? lastModified = page.Statements.Count == 0 ? null : page.Statements.Max(s => s.Stored);
        return JsonOrMultipart(WriteStatementResult(statements, more), query.Attachments, lastModified);
    }

    private static IResult JsonOrMultipart(string json, bool attachments, DateTimeOffset? lastModified)
    {
        if (!attachments)
        {
            return XapiResults.Json(json, lastModified);
        }

        // attachments=true asks for the multipart format. Attachment data is not stored yet (multipart requests are
        // rejected), so the JSON is the only part.
        var boundary = "xapi-" + Guid.NewGuid().ToString("N");
        var multipart = $"--{boundary}\r\nContent-Type: application/json\r\n\r\n{json}\r\n--{boundary}--\r\n";
        return XapiResults.Body($"multipart/mixed; boundary={boundary}", Encoding.UTF8.GetBytes(multipart), lastModified);
    }

    private static string WriteStatementResult(List<string> statements, string more)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("statements");
            foreach (var statement in statements)
            {
                writer.WriteRawValue(statement, skipInputValidation: true);
            }

            writer.WriteEndArray();
            writer.WriteString("more", more);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
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

    // 4.1.6.1 "Language Filtering Requirements for Canonical Format Statements": the Accept-Language ranges in
    // preference order, applied to each language map separately
    private static List<string> AcceptLanguages(HttpRequest request) =>
        [.. request.GetTypedHeaders().AcceptLanguage
            .Where(l => l.Quality is not 0)
            .OrderByDescending(l => l.Quality ?? 1)
            .Select(l => l.Value.ToString())];

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
