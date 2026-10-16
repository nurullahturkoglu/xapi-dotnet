// Sends one statement to an LRS and reads it back, then sends one more through a file-backed outbox.
//
//   XAPI_ENDPOINT   the LRS endpoint, for example http://localhost:8080/xapi/
//   XAPI_KEY        Basic auth key
//   XAPI_SECRET     Basic auth secret
//   XAPI_VERSION    optional, 1.0.3 or 2.0.0; when unset the client asks the LRS (GET about)

using Xapi;
using Xapi.Client;
using Xapi.Client.Outbox;
using Xapi.Json;
using Xapi.Validation;

var endpoint = Environment.GetEnvironmentVariable("XAPI_ENDPOINT");
var key = Environment.GetEnvironmentVariable("XAPI_KEY");
var secret = Environment.GetEnvironmentVariable("XAPI_SECRET");
var versionText = Environment.GetEnvironmentVariable("XAPI_VERSION");
if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(secret))
{
    Console.Error.WriteLine("Set XAPI_ENDPOINT, XAPI_KEY and XAPI_SECRET (and optionally XAPI_VERSION).");
    return 2;
}

XapiVersion? requested = null;
if (!string.IsNullOrEmpty(versionText))
{
    if (!XapiVersion.TryParseHeader(versionText, out var parsed))
    {
        Console.Error.WriteLine($"XAPI_VERSION \"{versionText}\" is not an xAPI version.");
        return 2;
    }

    requested = parsed;
}

using var http = new HttpClient();
var client = new XapiClient(http, new XapiClientOptions
{
    Endpoint = new Uri(endpoint),
    Version = requested,
    Credentials = XapiCredentials.Basic(key, secret),
});

var statement = new Statement(
    Agent.FromMbox("learner@example.com", "Sample Learner"),
    Verbs.Completed,
    new Activity(new Iri("https://example.com/activities/xapi-dotnet-sample"))
    {
        Definition = new ActivityDefinition { Name = LanguageMap.Create("en-US", "xapi-dotnet sample") },
    })
{
    Result = new Result { Completion = true, Duration = XapiDuration.Parse("PT1M30S") },
    // Milliseconds are what most LRSs keep, so the equivalence check below compares like with like
    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
};

var version = await client.GetVersionAsync();
var id = await client.SaveStatementAsync(statement);
var stored = await client.GetStatementAsync(id);
if (stored is null || !StatementEquivalence.AreEquivalent(statement with { Id = id }, stored))
{
    Console.Error.WriteLine($"Statement {id} did not come back as it was sent.");
    return 1;
}

Console.WriteLine($"xAPI {version}: the LRS stored statement {id}.");
Console.WriteLine(XapiJson.SerializeStatement(stored, version));

var outboxDirectory = Path.Combine(Path.GetTempPath(), "xapi-dotnet-sample-outbox", Guid.NewGuid().ToString("N"));
try
{
    Guid queuedId;
    using (var store = new FileOutboxStore(outboxDirectory))
    {
        await using var outbox = new StatementOutbox(client, store);
        queuedId = await outbox.EnqueueAsync(statement with { Id = null });
        var flush = await outbox.FlushAsync();
        if (flush.Delivered != 1)
        {
            Console.Error.WriteLine($"The outbox did not deliver statement {queuedId}: {flush.Status.LastError}");
            return 1;
        }
    }

    if (await client.GetStatementAsync(queuedId) is null)
    {
        Console.Error.WriteLine($"The LRS does not have statement {queuedId} that the outbox delivered.");
        return 1;
    }

    Console.WriteLine($"The outbox delivered statement {queuedId}.");
}
finally
{
    Directory.Delete(outboxDirectory, recursive: true);
}

return 0;
