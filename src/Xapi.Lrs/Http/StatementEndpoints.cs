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

    public static async Task<IResult> PutAsync(HttpContext http, StatementWriter writer, IOptions<XapiLrsOptions> options)
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

        var (body, bodyError) = await StatementBodyReader.ReadAsync(http.Request, options.Value.MaxAttachmentBytes, http.RequestAborted).ConfigureAwait(false);
        if (body is null)
        {
            return bodyError!;
        }

        var parsed = XapiJson.ParseStatement(body.Json, request.Version);
        if (!parsed.IsSuccess)
        {
            return XapiResults.Problem(StatusCodes.Status400BadRequest, "The statement is not valid.", parsed.Errors);
        }

        var statement = parsed.Value;
        if (statement.Id is { } id && id != statementId)
        {
            return BadRequest($"The statement id {id} does not match the statementId parameter {statementId}.");
        }

        if (CheckAttachments([statement], body, out var attachments) is { } attachmentError)
        {
            return attachmentError;
        }

        var (result, _) = await writer.WriteAsync([statement with { Id = statementId }], attachments, request.Version, request.Caller, http.RequestAborted).ConfigureAwait(false);
        return result.IsConflict
            ? Conflict(result.ConflictingStatementId.Value)
            : XapiResults.Status(StatusCodes.Status204NoContent);
    }

    public static async Task<IResult> PostAsync(HttpContext http, StatementWriter writer, IOptions<XapiLrsOptions> options)
    {
        var request = XapiRequestFeature.Get(http);
        if (http.Request.Query.Count > 0)
        {
            // A well-formed xAPI 1.0.x Alternate Request Syntax request was already rewritten by
            // AlternateRequestSyntaxMiddleware; anything else with a method parameter is rejected here.
            return BadRequest(http.Request.Query.ContainsKey(AlternateRequestSyntax.MethodParameter)
                ? AlternateRequestSyntax.RejectionMessage(request.Version)
                : RequestReading.CheckParameters(http.Request.Query, []) ?? "No parameters are allowed.");
        }

        var (body, bodyError) = await StatementBodyReader.ReadAsync(http.Request, options.Value.MaxAttachmentBytes, http.RequestAborted).ConfigureAwait(false);
        if (body is null)
        {
            return bodyError!;
        }

        var parsed = XapiJson.ParseStatements(body.Json, request.Version);
        if (!parsed.IsSuccess)
        {
            var duplicate = parsed.Errors.Any(e => e.Code == XapiErrorCodes.BatchDuplicateId);
            return XapiResults.Problem(
                StatusCodes.Status400BadRequest,
                duplicate ? "The batch contains the same statement id more than once." : "The statements are not valid.",
                parsed.Errors);
        }

        if (CheckAttachments(parsed.Value, body, out var attachments) is { } attachmentError)
        {
            return attachmentError;
        }

        var (result, ids) = await writer.WriteAsync(parsed.Value, attachments, request.Version, request.Caller, http.RequestAborted).ConfigureAwait(false);
        return result.IsConflict
            ? Conflict(result.ConflictingStatementId.Value)
            : XapiResults.Json(WriteIds(ids));
    }

    // IEEE 9274.1.1, 4.1.3: attachment data is matched to Attachment objects by SHA-2
    private static IResult? CheckAttachments(IReadOnlyList<Statement> statements, StatementBody body, out List<IReadOnlyList<AttachmentContent>> attachments) =>
        StatementAttachments.Match(statements, body.Parts, out attachments) is { } mismatch ? BadRequest(mismatch) : null;

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
        return await JsonOrMultipartAsync(store, rendered[0], [stored], attachments, stored.Stored, http.RequestAborted).ConfigureAwait(false);
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
        return await JsonOrMultipartAsync(store, WriteStatementResult(statements, more), page.Statements, query.Attachments, lastModified, http.RequestAborted).ConfigureAwait(false);
    }

    // 4.1.6.1 GET "attachments": true asks for the multipart format with the data of every attachment the LRS
    // holds for the returned statements; false for application/json without attachment data
    private static async Task<IResult> JsonOrMultipartAsync(
        IStatementStore store,
        string json,
        IReadOnlyList<StoredStatement> statements,
        bool attachments,
        DateTimeOffset? lastModified,
        CancellationToken cancellationToken)
    {
        if (!attachments)
        {
            return XapiResults.Json(json, lastModified);
        }

        var data = await store.GetAttachmentsAsync([.. statements.Select(s => s.Id)], cancellationToken).ConfigureAwait(false);
        var (contentType, body) = MultipartResponse.Write(json, data);
        return XapiResults.Body(contentType, body, lastModified);
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
