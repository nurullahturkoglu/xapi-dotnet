using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xapi.Client.Internal;
using Xapi.Json;
using Xapi.Validation;

namespace Xapi.Client;

public sealed partial class XapiClient
{
    private const string StatementsResource = "statements";

    /// <inheritdoc/>
    public async Task<Guid> SaveStatementAsync(Statement statement, IReadOnlyList<AttachmentPayload>? attachments = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(statement);
        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        if (_validateBeforeSend)
        {
            ThrowIfInvalid([statement], attachments, version, batch: false);
        }

        var content = CreateStatementsContent(JsonBytes.From(XapiJson.SerializeStatement(statement, version)), attachments);
        if (statement.Id is { } id)
        {
            var query = new QueryBuilder().Add("statementId", id);
            using var putResponse = await SendAsync(HttpMethod.Put, StatementsResource, query, content, null, version, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(putResponse, cancellationToken).ConfigureAwait(false);
            return id;
        }

        using var response = await SendAsync(HttpMethod.Post, StatementsResource, null, content, null, version, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var ids = await ReadIdsAsync(response, cancellationToken).ConfigureAwait(false);
        return ids.Count == 1 ? ids[0] : throw Unusable(response, null, $"returned {ids.Count} Statement ids for one Statement");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>> SaveStatementsAsync(IReadOnlyList<Statement> statements, IReadOnlyList<AttachmentPayload>? attachments = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(statements);
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is null)
            {
                throw new ArgumentException($"The Statement at index {i} is null.", nameof(statements));
            }
        }

        if (statements.Count == 0)
        {
            return [];
        }

        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        if (_validateBeforeSend)
        {
            ThrowIfInvalid(statements, attachments, version, batch: true);
        }

        var content = CreateStatementsContent(JsonBytes.From(XapiJson.SerializeStatements(statements, version)), attachments);
        using var response = await SendAsync(HttpMethod.Post, StatementsResource, null, content, null, version, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var ids = await ReadIdsAsync(response, cancellationToken).ConfigureAwait(false);
        return ids.Count == statements.Count
            ? ids
            : throw Unusable(response, null, $"returned {ids.Count} Statement ids for {statements.Count} Statements");
    }

    /// <inheritdoc/>
    public Task<Statement?> GetStatementAsync(Guid id, StatementFormat format = StatementFormat.Exact, CancellationToken cancellationToken = default) =>
        GetSingleStatementAsync("statementId", id, format, cancellationToken);

    /// <inheritdoc/>
    public Task<Statement?> GetVoidedStatementAsync(Guid id, StatementFormat format = StatementFormat.Exact, CancellationToken cancellationToken = default) =>
        GetSingleStatementAsync("voidedStatementId", id, format, cancellationToken);

    /// <inheritdoc/>
    public async Task<StatementResult> QueryStatementsAsync(StatementQuery query, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(query);
        if (query.Limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, "StatementQuery.Limit must not be negative.");
        }

        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        var parameters = new QueryBuilder()
            .Add("agent", query.Agent, version)
            .Add("verb", query.Verb)
            .Add("activity", query.Activity)
            .Add("registration", query.Registration)
            .AddFlag("related_activities", query.RelatedActivities)
            .AddFlag("related_agents", query.RelatedAgents)
            .Add("since", query.Since)
            .Add("until", query.Until)
            .Add("limit", query.Limit)
            .Add("format", query.Format)
            .AddFlag("attachments", query.Attachments)
            .AddFlag("ascending", query.Ascending);
        using var response = await SendAsync(HttpMethod.Get, StatementsResource, parameters, null, null, version, cancellationToken).ConfigureAwait(false);
        return await ReadStatementResultAsync(response, version, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<StatementResult> GetMoreStatementsAsync(Uri more, CancellationToken cancellationToken = default)
    {
        var uri = ResolveMore(Guard.NotNull(more));
        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        using var response = await SendAsync(HttpMethod.Get, uri, null, null, version, cancellationToken).ConfigureAwait(false);
        return await ReadStatementResultAsync(response, version, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<Statement> QueryAllStatementsAsync(StatementQuery query, CancellationToken cancellationToken = default) =>
        // The data would be dropped, so do not ask for it
        QueryAllCoreAsync(Guard.NotNull(query) with { Attachments = false }, cancellationToken);

    // Separate from the public method so a null query throws at the call, not at the first MoveNextAsync
    private async IAsyncEnumerable<Statement> QueryAllCoreAsync(StatementQuery query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var page = await QueryStatementsAsync(query, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            foreach (var statement in page.Statements)
            {
                yield return statement;
            }

            if (page.More is null)
            {
                yield break;
            }

            page = await GetMoreStatementsAsync(page.More, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task<Guid> VoidStatementAsync(Guid statementId, Actor actor, CancellationToken cancellationToken = default) =>
        SaveStatementAsync(new Statement(Guard.NotNull(actor), Verbs.Voided, new StatementRef(statementId)), null, cancellationToken);

    private async Task<Statement?> GetSingleStatementAsync(string parameter, Guid id, StatementFormat format, CancellationToken cancellationToken)
    {
        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder().Add(parameter, id).Add("format", format);
        using var response = await SendAsync(HttpMethod.Get, StatementsResource, query, null, null, version, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var (json, _) = await ReadStatementBodyAsync(response, cancellationToken).ConfigureAwait(false);
        return ParsedOrThrow(XapiJson.ParseStatement(json, version, XapiReadMode.Lenient), response, json, "a Statement");
    }

    private Uri ResolveMore(Uri more)
    {
        // IEEE 9274.1.1: "more" is a relative IRL with the full path. Keeping the endpoint's origin means a
        // malicious or misconfigured LRS cannot send our credentials elsewhere, and works behind proxies that
        // return internal host names.
        var pathAndQuery = more.IsAbsoluteUri ? more.PathAndQuery : more.OriginalString;
        if (pathAndQuery.Length == 0)
        {
            throw new ArgumentException("The more IRL is empty.", nameof(more));
        }

        var origin = new Uri(_endpoint.GetLeftPart(UriPartial.Authority) + "/");
        var resolved = pathAndQuery[0] == '/' ? new Uri(origin, pathAndQuery) : new Uri(_endpoint, pathAndQuery);
        if (Uri.Compare(resolved, _endpoint, UriComponents.SchemeAndServer, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            throw new ArgumentException($"The more IRL \"{more}\" does not stay on the LRS endpoint's host.", nameof(more));
        }

        return resolved;
    }

    private static ByteArrayContent CreateStatementsContent(byte[] json, IReadOnlyList<AttachmentPayload>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return CreateContent(json, JsonMediaType);
        }

        var boundary = MultipartMixed.NewBoundary();
        while (!MultipartMixed.IsSafeBoundary(boundary, json, attachments))
        {
            boundary = MultipartMixed.NewBoundary();
        }

        var content = new ByteArrayContent(MultipartMixed.Write(boundary, json, attachments));
        content.Headers.ContentType = new MediaTypeHeaderValue("multipart/mixed")
        {
            Parameters = { new NameValueHeaderValue("boundary", boundary) },
        };
        return content;
    }

    private static void ThrowIfInvalid(IReadOnlyList<Statement> statements, IReadOnlyList<AttachmentPayload>? attachments, XapiVersion version, bool batch)
    {
        var errors = new List<XapiError>(batch ? StatementValidator.ValidateBatch(statements, version) : StatementValidator.Validate(statements[0], version));

        // IEEE 9274.1.1, 4.1.3: the LRS rejects Attachments that have neither a fileUrl nor a matching part
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < (attachments?.Count ?? 0); i++)
        {
            available.Add(attachments![i]?.Sha2 ?? throw new ArgumentException($"The attachment at index {i} is null.", nameof(attachments)));
        }

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < statements.Count; i++)
        {
            var path = batch ? $"$[{i}]" : "$";
            CheckAttachments(statements[i].Attachments, path + ".attachments");
            if (statements[i].Object is SubStatement subStatement)
            {
                CheckAttachments(subStatement.Attachments, path + ".object.attachments");
            }
        }

        for (var i = 0; i < (attachments?.Count ?? 0); i++)
        {
            if (!referenced.Contains(attachments![i].Sha2))
            {
                errors.Add(new XapiError(
                    "attachment.payload.unreferenced",
                    $"attachments[{i}]",
                    $"No Attachment object has the SHA-2 {attachments[i].Sha2} of this attachment data."));
            }
        }

        if (errors.Count > 0)
        {
            throw new XapiValidationException(errors);
        }

        void CheckAttachments(XapiList<Attachment> list, string listPath)
        {
            for (var j = 0; j < list.Count; j++)
            {
                referenced.Add(list[j].Sha2);
                if (list[j].FileUrl is null && !available.Contains(list[j].Sha2))
                {
                    errors.Add(new XapiError(
                        "attachment.payload.missing",
                        $"{listPath}[{j}]",
                        $"The Attachment has no fileUrl and no attachment data with the SHA-2 {list[j].Sha2} was given."));
                }
            }
        }
    }

    private static async Task<IReadOnlyList<Guid>> ReadIdsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var values = await ReadStringArrayAsync(response, cancellationToken).ConfigureAwait(false);
        var ids = new Guid[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            if (!Guid.TryParseExact(values[i], "D", out ids[i]))
            {
                throw Unusable(response, null, $"\"{values[i]}\" is not a Statement id");
            }
        }

        return ids;
    }

    private static async Task<StatementResult> ReadStatementResultAsync(HttpResponseMessage response, XapiVersion version, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var (json, attachments) = await ReadStatementBodyAsync(response, cancellationToken).ConfigureAwait(false);

        XapiList<Statement> statements;
        Uri? more = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("statements", out var statementsElement)
                || statementsElement.ValueKind != JsonValueKind.Array)
            {
                throw Unusable(response, json, "the body is not a StatementResult object");
            }

            statements = ParsedOrThrow(XapiJson.ParseStatements(statementsElement, version, XapiReadMode.Lenient), response, json, "a StatementResult object");
            if (root.TryGetProperty("more", out var moreElement) && moreElement.ValueKind == JsonValueKind.String
                && moreElement.GetString() is { Length: > 0 } moreText)
            {
                more = Uri.TryCreate(moreText, UriKind.RelativeOrAbsolute, out var parsed)
                    ? parsed
                    : throw Unusable(response, json, $"its \"more\" value \"{moreText}\" is not an IRL");
            }
        }
        catch (JsonException exception)
        {
            throw Unusable(response, json, "the body is not JSON", exception);
        }

        return new StatementResult(statements, more)
        {
            ConsistentThrough = ReadConsistentThrough(response),
            Attachments = attachments,
        };
    }

    /// <summary>Returns the Statement JSON and, for a <c>multipart/mixed</c> response, the attachment parts.</summary>
    private static async Task<(byte[] Json, XapiList<AttachmentPayload> Attachments)> ReadStatementBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
        var contentType = ContentTypeOf(response);
        if (!string.Equals(contentType?.MediaType, "multipart/mixed", StringComparison.OrdinalIgnoreCase))
        {
            return (body, XapiList<AttachmentPayload>.Empty);
        }

        var boundary = contentType!.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, "boundary", StringComparison.OrdinalIgnoreCase))?.Value?.Trim('"');
        if (string.IsNullOrEmpty(boundary))
        {
            throw Unusable(response, body, "its multipart/mixed Content-Type has no boundary");
        }

        List<MultipartPart> parts;
        try
        {
            parts = MultipartMixed.Read(body, boundary!);
        }
        catch (FormatException exception)
        {
            throw Unusable(response, body, "its multipart/mixed body is malformed", exception);
        }

        if (parts.Count == 0)
        {
            throw Unusable(response, body, "its multipart/mixed body has no parts");
        }

        var payloads = new List<AttachmentPayload>(parts.Count - 1);
        foreach (var part in parts.Skip(1))
        {
            var computed = Sha256Hex.Compute(part.Body.Span);
            var declared = part.Header(MultipartMixed.HashHeader);
            if (declared is not null && !string.Equals(declared, computed, StringComparison.OrdinalIgnoreCase))
            {
                throw Unusable(response, body, $"the data of attachment {declared} has the SHA-2 {computed}");
            }

            payloads.Add(new AttachmentPayload(declared ?? computed, part.Header("Content-Type") ?? "application/octet-stream", part.Body));
        }

        return (parts[0].Body.ToArray(), XapiList.CreateRange(payloads));
    }
}
