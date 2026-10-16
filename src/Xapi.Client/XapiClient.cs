using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Xapi.Client.Internal;
using Xapi.Json;

namespace Xapi.Client;

/// <summary>
/// An <see cref="IXapiClient"/> over an <see cref="HttpClient"/>. Every request carries the
/// <c>X-Experience-API-Version</c> header and, if configured, credentials.
/// </summary>
/// <remarks>
/// <para>
/// The client keeps no per-request state and can be shared between threads. The <see cref="HttpClient"/> is not
/// disposed by the client. Configure timeouts, proxies and handlers on the <see cref="HttpClient"/>; the transport
/// can be any <see cref="HttpMessageHandler"/>.
/// </para>
/// <para>
/// There is no built-in retry. With dependency injection, add one to the <see cref="HttpClient"/> pipeline; see
/// <c>AddXapiClient</c>. For offline use and guaranteed delivery, use an outbox instead.
/// </para>
/// </remarks>
public sealed partial class XapiClient : IXapiClient
{
    private const string VersionHeader = "X-Experience-API-Version";
    private const string ConsistentThroughHeader = "X-Experience-API-Consistent-Through";
    private const string JsonMediaType = "application/json";
    private const int MessageBodyExcerpt = 500;

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly XapiCredentials? _credentials;
    private readonly bool _validateBeforeSend;
    private readonly VersionNegotiation _version;

    /// <summary>Creates a client.</summary>
    /// <param name="httpClient">The HTTP client to send requests with.</param>
    /// <param name="options">The settings. They are copied; later changes have no effect.</param>
    /// <exception cref="ArgumentException"><see cref="XapiClientOptions.Endpoint"/> is missing or not absolute.</exception>
    public XapiClient(HttpClient httpClient, XapiClientOptions options)
        : this(httpClient, options, null)
    {
    }

    internal XapiClient(HttpClient httpClient, XapiClientOptions options, VersionNegotiation? sharedNegotiation)
    {
        _http = Guard.NotNull(httpClient);
        Guard.NotNull(options);
        _endpoint = NormalizeEndpoint(options);
        _credentials = options.Credentials;
        _validateBeforeSend = options.ValidateBeforeSend;
        _version = options.Version is { } version ? new VersionNegotiation(version) : sharedNegotiation ?? new VersionNegotiation();
    }

    /// <inheritdoc/>
    public Task<XapiVersion> GetVersionAsync(CancellationToken cancellationToken = default) =>
        _version.GetAsync(NegotiateAsync, cancellationToken);

    /// <inheritdoc/>
    public async Task<About> GetAboutAsync(CancellationToken cancellationToken = default) =>
        await GetAboutAsync(_version.Known ?? XapiVersion.V2_0_0, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<Person> GetPersonAsync(Agent agent, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(agent);
        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder().Add("agent", agent, version);
        using var response = await SendAsync(HttpMethod.Get, "agents", query, null, null, version, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var body = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
        return ParsedOrThrow(XapiJson.ParsePerson(body, version, XapiReadMode.Lenient), response, body, "a Person object");
    }

    /// <inheritdoc/>
    public async Task<Activity?> GetActivityAsync(Iri activityId, CancellationToken cancellationToken = default)
    {
        Guard.NotDefault(activityId);
        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder().Add("activityId", activityId);
        using var response = await SendAsync(HttpMethod.Get, "activities", query, null, null, version, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var body = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
        return ParsedOrThrow(XapiJson.ParseActivity(body, version, XapiReadMode.Lenient), response, body, "an Activity");
    }

    private static Uri NormalizeEndpoint(XapiClientOptions options)
    {
        var endpoint = options.Endpoint;
        if (endpoint is null)
        {
            throw new ArgumentException("XapiClientOptions.Endpoint is required.", nameof(options));
        }

        if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"XapiClientOptions.Endpoint must be an absolute http or https URI, not \"{endpoint}\".", nameof(options));
        }

        // Without the slash, relative resource names would replace the last path segment
        return endpoint.AbsolutePath[endpoint.AbsolutePath.Length - 1] == '/'
            ? endpoint
            : new UriBuilder(endpoint) { Path = endpoint.AbsolutePath + "/" }.Uri;
    }

    private async Task<XapiVersion> NegotiateAsync()
    {
        // Not cancellable by one caller: others may be waiting for the same negotiation. HttpClient.Timeout bounds it.
        var about = await GetAboutAsync(XapiVersion.V2_0_0, CancellationToken.None).ConfigureAwait(false);
        XapiVersion? best = null;
        foreach (var text in about.Version)
        {
            if (XapiVersion.TryParseHeader(text, out var candidate) && (best is null || candidate.IsV2))
            {
                best = candidate;
            }
        }

        return best ?? throw new NotSupportedException(
            $"The LRS at {_endpoint} supports none of the xAPI versions this client speaks (2.0.0, 1.0.3). Its About resource lists: {string.Join(", ", about.Version)}.");
    }

    private async Task<About> GetAboutAsync(XapiVersion headerVersion, CancellationToken cancellationToken)
    {
        // IEEE 9274.1.1, 4.1.6.7: the About resource must not reject a request because of its version header
        using var response = await SendAsync(HttpMethod.Get, "about", null, null, null, headerVersion, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var body = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
        return ParsedOrThrow(XapiJson.ParseAbout(body, XapiVersion.V2_0_0, XapiReadMode.Lenient), response, body, "an About object");
    }

    private Uri ResourceUri(string resource, QueryBuilder? query) => new(_endpoint, resource + query);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string resource,
        QueryBuilder? query,
        HttpContent? content,
        DocumentPrecondition? precondition,
        XapiVersion version,
        CancellationToken cancellationToken) =>
        await SendAsync(method, ResourceUri(resource, query), content, precondition, version, cancellationToken).ConfigureAwait(false);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        DocumentPrecondition? precondition,
        XapiVersion version,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.TryAddWithoutValidation(VersionHeader, version.HeaderValue);
        if (_credentials is not null)
        {
            request.Headers.Authorization = await _credentials.GetHeaderAsync(cancellationToken).ConfigureAwait(false);
        }

        precondition?.Apply(request.Headers);
        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Handlers other than SocketsHttpHandler may leave it unset; error messages use it
        response.RequestMessage ??= request;
        return response;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
        var text = body.Length == 0 ? null : Encoding.UTF8.GetString(body);
        var status = (int)response.StatusCode;
        var message = $"The LRS answered {status} {response.ReasonPhrase} to {Describe(response)}.{Excerpt(text)}";

        throw response.StatusCode switch
        {
            HttpStatusCode.Conflict => new XapiConflictException(text, message),
            HttpStatusCode.PreconditionFailed => new XapiPreconditionFailedException(text, message),
            _ => new XapiHttpException(response.StatusCode, text, message, RetryAfter(response)),
        };
    }

    private static XapiHttpException Unusable(HttpResponseMessage response, byte[]? body, string problem, Exception? innerException = null)
    {
        var text = body is null || body.Length == 0 ? null : Encoding.UTF8.GetString(body);
        var message = $"The LRS answered {Describe(response)} with {(int)response.StatusCode}, but {problem}.";
        return new XapiHttpException(response.StatusCode, text, message, null, innerException);
    }

    // Method and path only: the query can hold personal data (agent parameters)
    private static string Describe(HttpResponseMessage response) =>
        response.RequestMessage is { RequestUri: { } uri } request ? $"{request.Method} {uri.AbsolutePath}" : "the request";

    private static string Excerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var flat = text!.Replace("\r", " ").Replace('\n', ' ').Trim();
        return " Response: " + (flat.Length <= MessageBodyExcerpt ? flat : flat.Remove(MessageBodyExcerpt) + "…");
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var now = response.Headers.Date ?? DateTimeOffset.UtcNow;
            return date > now ? date - now : TimeSpan.Zero;
        }

        return null;
    }

    private static T ParsedOrThrow<T>(XapiParseResult<T> result, HttpResponseMessage response, byte[] body, string what)
    {
        if (result.IsSuccess)
        {
            return result.Value;
        }

        throw Unusable(response, body, $"the body is not {what}", new XapiValidationException(result.Errors));
    }

    private static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
#if NET
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
        cancellationToken.ThrowIfCancellationRequested();
        return response.Content is null ? [] : await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
    }

    private static async Task<IReadOnlyList<string>> ReadStringArrayAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var values = new List<string>(document.RootElement.GetArrayLength());
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String)
                    {
                        break;
                    }

                    values.Add(element.GetString()!);
                }

                if (values.Count == document.RootElement.GetArrayLength())
                {
                    return values;
                }
            }
        }
        catch (JsonException exception)
        {
            throw Unusable(response, body, "the body is not JSON", exception);
        }

        throw Unusable(response, body, "the body is not a JSON array of strings");
    }

    private static ByteArrayContent CreateContent(ReadOnlyMemory<byte> bytes, string contentType)
    {
        var content = MemoryMarshal.TryGetArray(bytes, out var segment)
            ? new ByteArrayContent(segment.Array!, segment.Offset, segment.Count)
            : new ByteArrayContent(bytes.ToArray());
        if (!content.Headers.TryAddWithoutValidation("Content-Type", contentType))
        {
            content.Dispose();
            throw new ArgumentException($"\"{contentType}\" is not a valid Content-Type.", nameof(contentType));
        }

        return content;
    }

    private static DateTimeOffset? ReadConsistentThrough(HttpResponseMessage response) =>
        ReadTimestampHeader(response.Headers, ConsistentThroughHeader);

    // HTTP wants an RFC 1123 date here, but some LRSs (lrsql among them) send an ISO 8601 timestamp, which the typed
    // header property turns into null
    private static DateTimeOffset? ReadLastModified(HttpResponseMessage response) =>
        response.Content is null ? null : response.Content.Headers.LastModified ?? ReadTimestampHeader(response.Content.Headers, "Last-Modified");

    private static DateTimeOffset? ReadTimestampHeader(HttpHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values))
        {
            return null;
        }

        var text = values.FirstOrDefault();
        if (XapiTimestamp.TryParse(text, XapiVersion.V2_0_0, out var timestamp))
        {
            return timestamp;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp) ? timestamp : null;
    }

    private static MediaTypeHeaderValue? ContentTypeOf(HttpResponseMessage response) => response.Content?.Headers.ContentType;
}
