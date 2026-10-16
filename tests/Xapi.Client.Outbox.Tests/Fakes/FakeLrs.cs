using System.Net;
using System.Text;
using Xapi.Json;
using Xapi.Validation;

namespace Xapi.Client.Outbox.Tests.Fakes;

/// <summary>
/// A minimal Statement resource behind an <see cref="HttpMessageHandler"/>: stores Statements by id, answers 409 to
/// a different Statement with a known id, and can be told to fail in the ways an outbox has to handle.
/// </summary>
internal sealed class FakeLrs : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, Statement> _byId = [];
    private readonly Queue<Func<LrsRequest, HttpResponseMessage>> _next = new();

    public List<LrsRequest> Requests { get; } = [];

    /// <summary>Statements in the order the LRS stored them.</summary>
    public List<Statement> Stored { get; } = [];

    /// <summary>Ids that are stored but voided: GET by statementId answers 404, by voidedStatementId 200.</summary>
    public HashSet<Guid> Voided { get; } = [];

    /// <summary>Answers 400 (or the returned status) to any request that contains a matching Statement.</summary>
    public Func<Statement, HttpStatusCode?>? Reject { get; set; }

    /// <summary>Answers 413 to POSTs with more Statements than this.</summary>
    public int? MaxStatementsPerRequest { get; set; }

    /// <summary>Some LRSs answer 409 even when the Statement is equivalent to the stored one.</summary>
    public bool ConflictOnEquivalent { get; set; }

    /// <summary>Throws <see cref="HttpRequestException"/> for every request.</summary>
    public bool Offline { get; set; }

    /// <summary>Stores the Statements of the next write, then throws as if the response was lost.</summary>
    public bool LoseNextResponse { get; set; }

    /// <summary>Awaited before each request is handled, after it is recorded.</summary>
    public Func<LrsRequest, Task>? BeforeHandle { get; set; }

    public int StatementWrites => Snapshot(() => Requests.Count(r => r.Method != HttpMethod.Get));

    /// <summary>Answers the next request with this responder instead of the normal behaviour.</summary>
    public FakeLrs Next(Func<LrsRequest, HttpResponseMessage> responder)
    {
        lock (_lock)
        {
            _next.Enqueue(responder);
        }

        return this;
    }

    public FakeLrs Next(HttpStatusCode status, string body = "", Action<HttpResponseMessage>? configure = null) =>
        Next(_ =>
        {
            var response = Response(status, body);
            configure?.Invoke(response);
            return response;
        });

    public FakeLrs NextThrows(Exception exception) => Next(_ => throw exception);

    public void Store(Statement statement)
    {
        lock (_lock)
        {
            _byId[statement.Id!.Value] = statement;
            Stored.Add(statement);
        }
    }

    public T Snapshot<T>(Func<T> read)
    {
        lock (_lock)
        {
            return read();
        }
    }

    public List<Guid> StoredIds => Snapshot(() => Stored.Select(s => s.Id!.Value).ToList());

    public static HttpResponseMessage Response(HttpStatusCode status, string body = "", string contentType = "text/plain") =>
        new(status) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) { Headers = { { "Content-Type", contentType } } } };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if NET
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
#else
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
#endif
        var recorded = new LrsRequest(request.Method, request.RequestUri!, body);
        Func<LrsRequest, HttpResponseMessage>? next;
        lock (_lock)
        {
            Requests.Add(recorded);
            next = _next.Count > 0 ? _next.Dequeue() : null;
        }

        if (BeforeHandle is { } before)
        {
            await before(recorded);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Offline)
        {
            throw new HttpRequestException("No route to host (fake).");
        }

        if (next is not null)
        {
            return next(recorded);
        }

        lock (_lock)
        {
            return Handle(recorded);
        }
    }

    private HttpResponseMessage Handle(LrsRequest request)
    {
        if (request.Method == HttpMethod.Get)
        {
            return Get(request);
        }

        var statements = request.Statements;
        if (MaxStatementsPerRequest is { } max && statements.Count > max)
        {
            return Response(HttpStatusCode.RequestEntityTooLarge, "too many statements");
        }

        foreach (var statement in statements)
        {
            if (Reject?.Invoke(statement) is { } status)
            {
                return Response(status, $"{{\"error\":\"rejected {statement.Id}\"}}", "application/json");
            }
        }

        var fresh = new List<Statement>();
        foreach (var statement in statements)
        {
            if (_byId.TryGetValue(statement.Id!.Value, out var existing))
            {
                if (ConflictOnEquivalent || !StatementEquivalence.AreEquivalent(existing, statement))
                {
                    return Response(HttpStatusCode.Conflict, $"conflict on {statement.Id}");
                }
            }
            else
            {
                fresh.Add(statement);
            }
        }

        foreach (var statement in fresh)
        {
            Store(statement with { Stored = DateTimeOffset.UtcNow, Version = "2.0.0" });
        }

        if (LoseNextResponse)
        {
            LoseNextResponse = false;
            throw new HttpRequestException("The connection was reset after the request was sent (fake).");
        }

        return request.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            // Like lrsql: only the ids of Statements stored by this request
            : Response(HttpStatusCode.OK, "[" + string.Join(",", fresh.Select(s => $"\"{s.Id}\"")) + "]", "application/json");
    }

    private HttpResponseMessage Get(LrsRequest request)
    {
        if (request.Query("statementId") is { } id && _byId.TryGetValue(Guid.Parse(id), out var statement) && !Voided.Contains(statement.Id!.Value))
        {
            return Response(HttpStatusCode.OK, XapiJson.SerializeStatement(statement, XapiVersion.V2_0_0), "application/json");
        }

        if (request.Query("voidedStatementId") is { } voidedId && _byId.TryGetValue(Guid.Parse(voidedId), out var voided) && Voided.Contains(voided.Id!.Value))
        {
            return Response(HttpStatusCode.OK, XapiJson.SerializeStatement(voided, XapiVersion.V2_0_0), "application/json");
        }

        return Response(HttpStatusCode.NotFound);
    }
}

internal sealed record LrsRequest(HttpMethod Method, Uri Uri, string Body)
{
    /// <summary>The Statements in a PUT or POST body.</summary>
    public IReadOnlyList<Statement> Statements => Body.TrimStart() is { Length: > 0 } trimmed && trimmed[0] == '['
        ? XapiJson.ParseStatements(Body, XapiVersion.V2_0_0, XapiReadMode.Lenient).GetValueOrThrow()
        : [XapiJson.ParseStatement(Body, XapiVersion.V2_0_0, XapiReadMode.Lenient).GetValueOrThrow()];

    public IReadOnlyList<Guid> StatementIds => [.. Statements.Select(s => s.Id!.Value)];

    public string? Query(string name)
    {
        foreach (var pair in Uri.Query.TrimStart('?').Split('&'))
        {
            var parts = pair.Split('=');
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == name)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
