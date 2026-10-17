using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Http;

// The State, Activity Profile and Agent Profile Resources (IEEE 9274.1.1, 4.1.6.2, 4.1.6.5, 4.1.6.6; xAPI
// 1.0.3 Communication 2.2-2.7) and their concurrency rules (4.1.4; 1.0.3 Communication 3.1).
internal static class DocumentEndpoints
{
    private const string OctetStream = "application/octet-stream";

    private enum Operation
    {
        Get,
        Write,
        Delete,
    }

    public static async Task<IResult> GetAsync(HttpContext http, IDocumentStore store, DocumentResource resource)
    {
        if (!TryReadRequest(http, resource, Operation.Get, out var request, out var error))
        {
            return error;
        }

        if (request.Id is { } id)
        {
            var document = await store.GetAsync(request.Scope, id, http.RequestAborted).ConfigureAwait(false);
            if (document is null)
            {
                return XapiResults.Problem(StatusCodes.Status404NotFound, $"No document with {IdParameter(resource)} '{id}' was found.");
            }

            SetETag(http, document.ETag);
            if (!IsJson(document.ContentType))
            {
                // The content type is whatever the client stored, text/html or image/svg+xml included. A browser
                // sent here, on the origin of the app that hosts the LRS, must not render it as a page.
                var headers = http.Response.Headers;
                headers.ContentDisposition = "attachment";
                headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            }

            return XapiResults.Body(document.ContentType, document.Content, document.Updated);
        }

        var documents = await store.ListAsync(request.Scope, request.Since, http.RequestAborted).ConfigureAwait(false);
        var json = WriteIds(documents.Select(d => d.Id).Order(StringComparer.Ordinal));

        // The ETag requirement covers every GET of these resources (4.1.4); for an id list it identifies the list.
        // Last-Modified is the newest listed document (1.0.3 Communication 2.2, Last Modified).
        SetETag(http, EfDocumentStore.ETagOf(json));
        return XapiResults.Json(Encoding.UTF8.GetString(json), documents.Count > 0 ? documents.Max(d => d.Updated) : null);
    }

    public static async Task<IResult> PutAsync(HttpContext http, IDocumentStore store, IOptions<XapiLrsOptions> options, DocumentResource resource)
    {
        if (!TryReadRequest(http, resource, Operation.Write, out var request, out var error))
        {
            return error;
        }

        var body = await ReadBodyAsync(http.Request, options.Value.MaxDocumentBytes, http.RequestAborted).ConfigureAwait(false);
        if (body is null)
        {
            return TooLarge(options.Value.MaxDocumentBytes);
        }

        var contentType = ContentTypeOf(http.Request);
        var version = XapiRequestFeature.Get(http).Version;
        var preconditions = DocumentPreconditions.From(http.Request);
        IResult? failure = null;
        var stored = await store.ChangeAsync(request.Scope, request.Id!, current =>
        {
            failure = CheckPut(current, preconditions, version, resource);
            return failure is null ? DocumentChange.Store(body, contentType) : DocumentChange.None;
        }, http.RequestAborted).ConfigureAwait(false);

        return failure ?? Written(http, stored);
    }

    public static async Task<IResult> PostAsync(HttpContext http, IDocumentStore store, IOptions<XapiLrsOptions> options, DocumentResource resource)
    {
        if (!TryReadRequest(http, resource, Operation.Write, out var request, out var error))
        {
            return error;
        }

        var body = await ReadBodyAsync(http.Request, options.Value.MaxDocumentBytes, http.RequestAborted).ConfigureAwait(false);
        if (body is null)
        {
            return TooLarge(options.Value.MaxDocumentBytes);
        }

        var contentType = ContentTypeOf(http.Request);
        var postedJson = IsJson(contentType);
        if (postedJson && !JsonDocumentMerge.IsObject(body))
        {
            return BadRequest("A document posted as application/json must be a JSON object.");
        }

        var preconditions = DocumentPreconditions.From(http.Request);
        var max = options.Value.MaxDocumentBytes;
        IResult? failure = null;
        var stored = await store.ChangeAsync(request.Scope, request.Id!, current =>
        {
            failure = null;
            if (!preconditions.HoldFor(current))
            {
                failure = PreconditionFailed();
                return DocumentChange.None;
            }

            if (current is null)
            {
                return DocumentChange.Store(body, contentType);
            }

            // 4.1.6.2 POST: both documents must be JSON objects; nothing changes otherwise
            if (!postedJson || !IsJson(current.ContentType))
            {
                failure = BadRequest("POST merges JSON objects: the stored and the posted document must both have the content type application/json. Use PUT to replace the document.");
                return DocumentChange.None;
            }

            if (!JsonDocumentMerge.TryMerge(current.Content, body, out var merged))
            {
                failure = BadRequest("The stored document is not a JSON object, so the posted object cannot be merged into it. Use PUT to replace it.");
                return DocumentChange.None;
            }

            if (merged.Length > max)
            {
                failure = TooLarge(max);
                return DocumentChange.None;
            }

            return DocumentChange.Store(merged, current.ContentType);
        }, http.RequestAborted).ConfigureAwait(false);

        return failure ?? Written(http, stored);
    }

    public static async Task<IResult> DeleteAsync(HttpContext http, IDocumentStore store, DocumentResource resource)
    {
        if (!TryReadRequest(http, resource, Operation.Delete, out var request, out var error))
        {
            return error;
        }

        // Without a stateId, every state document of the Activity and Agent (and registration, if given) goes
        // (4.1.6.2, Multiple Document DELETE). A collection has no ETag, so preconditions do not apply.
        if (request.Id is not { } id)
        {
            await store.DeleteAllAsync(request.Scope, http.RequestAborted).ConfigureAwait(false);
            return XapiResults.Status(StatusCodes.Status204NoContent);
        }

        var preconditions = DocumentPreconditions.From(http.Request);
        IResult? failure = null;
        await store.ChangeAsync(request.Scope, id, current =>
        {
            failure = preconditions.HoldFor(current) ? null : PreconditionFailed();
            return failure is null ? DocumentChange.Delete : DocumentChange.None;
        }, http.RequestAborted).ConfigureAwait(false);

        // Deleting a document that does not exist succeeds: the outcome is the same
        return failure ?? XapiResults.Status(StatusCodes.Status204NoContent);
    }

    private static IResult? CheckPut(StoredDocument? current, DocumentPreconditions preconditions, XapiVersion version, DocumentResource resource)
    {
        if (preconditions.Any)
        {
            return preconditions.HoldFor(current) ? null : PreconditionFailed();
        }

        // 2.0 (4.1.4): a PUT without If-Match or If-None-Match must not replace an existing document, on all
        // three resources. 1.0.3 (Communication 3.1) exempts the State Resource, and requires one of the headers
        // on every profile PUT; the conformance suite expects 400 when the document does not exist yet.
        if (version.IsV2)
        {
            return current is null ? null : MissingPrecondition();
        }

        if (resource == DocumentResource.State)
        {
            return null;
        }

        return current is null
            ? BadRequest("xAPI 1.0.3 requires an If-Match or If-None-Match header on a PUT to a profile resource (Communication 3.1). Send If-None-Match: * to create the document.")
            : MissingPrecondition();
    }

    private static bool TryReadRequest(HttpContext http, DocumentResource resource, Operation operation, out DocumentRequest request, out IResult error)
    {
        request = default!;
        error = default!;
        var query = http.Request.Query;
        var version = XapiRequestFeature.Get(http).Version;
        var idName = IdParameter(resource);
        var usesActivity = resource is DocumentResource.State or DocumentResource.ActivityProfile;
        var usesAgent = resource is DocumentResource.State or DocumentResource.AgentProfile;

        List<string> allowed = [idName];
        if (usesActivity)
        {
            allowed.Add("activityId");
        }

        if (usesAgent)
        {
            allowed.Add("agent");
        }

        if (resource == DocumentResource.State)
        {
            allowed.Add("registration");
        }

        if (operation == Operation.Get)
        {
            allowed.Add("since");
        }

        if (RequestReading.CheckParameters(query, allowed) is { } parameterError)
        {
            error = BadRequest(parameterError);
            return false;
        }

        var activityId = default(Iri);
        if (usesActivity)
        {
            if (!query.TryGetValue("activityId", out var value))
            {
                error = BadRequest("The activityId parameter is required.");
                return false;
            }

            if (!Iri.TryParse(value.ToString(), out activityId))
            {
                error = BadRequest($"activityId '{value}' is not an IRI.");
                return false;
            }
        }

        Agent? agent = null;
        if (usesAgent && !RequestReading.TryReadAgent(query, version, out agent, out error))
        {
            return false;
        }

        Guid? registration = null;
        if (query.TryGetValue("registration", out var registrationValue))
        {
            if (!RequestReading.TryParseUuid(registrationValue, out var parsed))
            {
                error = BadRequest($"registration '{registrationValue}' is not a UUID.");
                return false;
            }

            registration = parsed;
        }

        string? id = query.TryGetValue(idName, out var idValue) ? idValue.ToString() : null;
        var idRequired = operation == Operation.Write || (operation == Operation.Delete && resource != DocumentResource.State);
        if (id is null && idRequired)
        {
            error = BadRequest($"The {idName} parameter is required.");
            return false;
        }

        DateTimeOffset? since = null;
        if (query.TryGetValue("since", out var sinceValue))
        {
            // 4.1.6.2 GET: since selects the multiple-document form, so it cannot be combined with the id
            if (id is not null)
            {
                error = BadRequest($"since cannot be combined with {idName}.");
                return false;
            }

            if (!XapiTimestamp.TryParse(sinceValue.ToString(), version, out var parsed))
            {
                error = BadRequest($"since '{sinceValue}' is not a timestamp.");
                return false;
            }

            since = parsed;
        }

        var scope = resource switch
        {
            DocumentResource.State => DocumentScope.ForState(activityId, agent!, registration),
            DocumentResource.ActivityProfile => DocumentScope.ForActivityProfile(activityId),
            _ => DocumentScope.ForAgentProfile(agent!),
        };
        request = new DocumentRequest(scope, id, since);
        return true;
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, long max, CancellationToken cancellationToken)
    {
        RequestReading.SetBodyLimit(request.HttpContext, max);
        if (request.ContentLength > max)
        {
            return null;
        }

        // Content-Length can be absent (chunked), so the limit is also enforced while reading
        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int read;
            while ((read = await request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > max)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return buffer.ToArray();
    }

    private static string ContentTypeOf(HttpRequest request) =>
        string.IsNullOrWhiteSpace(request.ContentType) ? OctetStream : request.ContentType;

    private static bool IsJson(string contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed) &&
        parsed.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);

    private static IResult Written(HttpContext http, StoredDocument? stored)
    {
        if (stored is not null)
        {
            SetETag(http, stored.ETag);
        }

        return XapiResults.Status(StatusCodes.Status204NoContent);
    }

    private static void SetETag(HttpContext http, string etag) => http.Response.Headers.ETag = $"\"{etag}\"";

    private static byte[] WriteIds(IEnumerable<string> ids)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var id in ids)
            {
                writer.WriteStringValue(id);
            }

            writer.WriteEndArray();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static string IdParameter(DocumentResource resource) => resource == DocumentResource.State ? "stateId" : "profileId";

    private static IResult BadRequest(string detail) => XapiResults.Problem(StatusCodes.Status400BadRequest, detail);

    private static IResult PreconditionFailed() =>
        XapiResults.Problem(StatusCodes.Status412PreconditionFailed, "The document has changed or does not match the If-Match or If-None-Match header. Get it again for its current ETag.");

    // 4.1.4: the response explains that the client should check the current state and send If-Match
    private static IResult MissingPrecondition() =>
        XapiResults.Problem(
            StatusCodes.Status409Conflict,
            "A document already exists here. Get it to check its current state, then send the PUT again with an If-Match header carrying its ETag.");

    private static IResult TooLarge(long max) =>
        XapiResults.Problem(StatusCodes.Status413PayloadTooLarge, $"The document is larger than the {max} bytes this LRS accepts.");

    private sealed record DocumentRequest(DocumentScope Scope, string? Id, DateTimeOffset? Since);
}
