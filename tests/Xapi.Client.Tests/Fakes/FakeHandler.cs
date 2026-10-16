using System.Net;
using System.Text;

namespace Xapi.Client.Tests.Fakes;

/// <summary>
/// Records every request (with its body read eagerly) and answers from a queue of responders, in order.
/// </summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly Queue<Func<RecordedRequest, Task<HttpResponseMessage>>> _responders = new();

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Answers requests that find the queue empty; null means such a request fails the test.</summary>
    public Func<RecordedRequest, Task<HttpResponseMessage>>? FallbackAsync { get; set; }

    /// <summary>A synchronous <see cref="FallbackAsync"/>, used when that is null.</summary>
    public Func<RecordedRequest, HttpResponseMessage>? Fallback { get; set; }

    public RecordedRequest Single => Assert.Single(Requests);

    public FakeHandler RespondAsync(Func<RecordedRequest, Task<HttpResponseMessage>> responder)
    {
        lock (_lock)
        {
            _responders.Enqueue(responder);
        }

        return this;
    }

    public FakeHandler Respond(Func<RecordedRequest, HttpResponseMessage> responder) =>
        RespondAsync(request => Task.FromResult(responder(request)));

    public FakeHandler Respond(HttpStatusCode status, string? body = null, string contentType = "application/json", Action<HttpResponseMessage>? configure = null) =>
        Respond(_ =>
        {
            var response = Response(status, body, contentType);
            configure?.Invoke(response);
            return response;
        });

    public FakeHandler RespondJson(string json) => Respond(HttpStatusCode.OK, json);

    public static HttpResponseMessage Response(HttpStatusCode status, string? body = null, string contentType = "application/json") =>
        Response(status, body is null ? null : Encoding.UTF8.GetBytes(body), contentType);

    public static HttpResponseMessage Response(HttpStatusCode status, byte[]? body, string contentType)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
        {
            response.Content = new ByteArrayContent(body);
            response.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A real handler sends nothing once the token is cancelled
        cancellationToken.ThrowIfCancellationRequested();
#if NET
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
#else
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync();
#endif
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        var contentType = request.Content?.Headers.TryGetValues("Content-Type", out var values) == true ? string.Join(", ", values) : null;
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers, contentType, body);

        Func<RecordedRequest, Task<HttpResponseMessage>> responder;
        lock (_lock)
        {
            Requests.Add(recorded);
            responder = _responders.Count > 0
                ? _responders.Dequeue()
                : FallbackAsync
                    ?? (Fallback is { } fallback ? r => Task.FromResult(fallback(r)) : (Func<RecordedRequest, Task<HttpResponseMessage>>?)null)
                    ?? throw new InvalidOperationException($"No response queued for {request.Method} {request.RequestUri}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await responder(recorded);
    }
}
