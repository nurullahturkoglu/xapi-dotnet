# Client

`Xapi.Client` is an `HttpClient`-based client for any LRS that speaks xAPI 1.0.3 or 2.0.0 (IEEE 9274.1.1). It
covers the Statement, State, Activity Profile, Agent Profile, Agents, Activities and About resources. It
targets netstandard2.0 and net10.0, uses no reflection, and the net10.0 build can be published with Native
AOT.

`IXapiClient` is the interface and `XapiClient` the implementation. Code that sends statements should depend
on `IXapiClient`, so tests can point it at a fake or at the embedded LRS (see
[samples/FakeLrsInTests](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/samples/FakeLrsInTests)).

## Creating a client

`new XapiClient(httpClient, options)` takes the `HttpClient` to send with and an `XapiClientOptions`
([getting-started.md](getting-started.md#send-a-statement) has a complete example):

| Option | Default | Meaning |
| --- | --- | --- |
| `Endpoint` | required | The base address of the LRS, for example `https://lrs.example.com/xapi/`. A missing trailing slash is added. |
| `Version` | `null` | The xAPI version to speak. `null` negotiates it with the LRS (see below). |
| `Credentials` | `null` | `XapiCredentials.Basic(key, secret)`, `XapiCredentials.Bearer(...)`, or `null` to send no `Authorization` header. |
| `ValidateBeforeSend` | `true` | Validate statements before sending them, and throw `XapiValidationException` instead of sending an invalid one. |

The options are copied when the client is created. The client keeps no per-request state, so one instance can
be shared by all threads. It does not dispose the `HttpClient`; timeouts, proxies and handlers are configured
there.

## Versions and negotiation

Every request carries the `X-Experience-API-Version` header, and the version also decides which validation
rules apply and how JSON is read and written.

When `Version` is `null`, the client reads the About resource before its first request and picks 2.0.0 if the
LRS lists a 2.0 version, otherwise 1.0.3 if it lists a 1.0 version. If it lists neither, the call throws
`NotSupportedException`. The result is kept for the life of the client (with dependency injection, for the
life of the service provider); a failed negotiation is tried again on the next call. `GetVersionAsync` returns
the version in use.

To skip negotiation, set the version:

<!-- snippet: client-version -->
```csharp
var client = new XapiClient(http, new XapiClientOptions
{
    Endpoint = new Uri("https://lrs.example.com/xapi/"),
    Credentials = XapiCredentials.Basic(key, secret),
    Version = XapiVersion.V1_0_3,
});
```

[versions.md](versions.md) describes what changes between the two versions.

## Credentials

`XapiCredentials.Basic` sends HTTP Basic credentials. `XapiCredentials.Bearer` takes a function that returns a
token; it is called before every request, so it should cache the token and refresh it when needed:

<!-- snippet: client-bearer -->
```csharp
var client = new XapiClient(http, new XapiClientOptions
{
    Endpoint = new Uri("https://lrs.example.com/xapi/"),
    Credentials = XapiCredentials.Bearer(tokens.GetAccessTokenAsync),
});
```

For any other scheme, leave `Credentials` empty and add the header in a `DelegatingHandler` on the
`HttpClient`.

## Dependency injection and retries

On net10.0, `AddXapiClient` registers `IXapiClient` and `XapiClient` as typed clients of a named `HttpClient`
(`XapiClientServiceCollectionExtensions.HttpClientName`, "Xapi.Client") from `IHttpClientFactory`. The
clients are transient, as typed clients are; the negotiated version is shared by all of them.

The client does not retry. To retry transient failures, add a resilience handler from
`Microsoft.Extensions.Http.Resilience` to the `HttpClient` that `AddXapiClient` returns:

<!-- snippet: client-resilience -->
```csharp
builder.Services
    .AddXapiClient(options =>
    {
        options.Endpoint = new Uri("https://lrs.example.com/xapi/");
        options.Credentials = XapiCredentials.Basic(key, secret);
    })
    .HttpClient.AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
```

`DisableForUnsafeHttpMethods` keeps retries to GET and other safe methods. A POST of a statement without an id
is not idempotent: if the response is lost, a retry stores the statement a second time. Statements that
already have an id are sent with PUT (or in a POST batch with their ids), and the LRS keeps the first copy, so
those are safe to repeat. For delivery that survives long outages and restarts, use the
[outbox](outbox.md) instead of long retry chains.

## Statements

<!-- snippet: client-statements -->
```csharp
var ids = await client.SaveStatementsAsync([statement, statement with { Verb = Verbs.Passed }]);

var stored = await client.GetStatementAsync(ids[0]);
var voidingId = await client.VoidStatementAsync(ids[1], learner);
var voided = await client.GetVoidedStatementAsync(ids[1]);
```

- `SaveStatementAsync` sends PUT with `statementId` when the statement has an `Id`, and POST otherwise. It
  returns the id and does not change the statement.
- `SaveStatementsAsync` sends one POST; the LRS stores all statements or none. An empty list returns at once.
- `GetStatementAsync` returns `null` when the LRS answers 404. Voided statements are only returned by
  `GetVoidedStatementAsync`. Both take a `StatementFormat` (`Exact`, `Ids` or `Canonical`).
- `VoidStatementAsync` stores a statement with the `voided` verb and a StatementRef to the voided one, and
  returns the voiding statement's id.

### Queries

<!-- snippet: client-query -->
```csharp
var query = new StatementQuery
{
    Agent = Agent.FromMbox("learner@example.com"),
    Verb = Verbs.Completed.Id,
    Since = DateTimeOffset.UtcNow.AddDays(-7),
};

await foreach (var statement in client.QueryAllStatementsAsync(query))
{
    Console.WriteLine($"{statement.Id} {statement.Timestamp:u}");
}
```

`StatementQuery` has a property for every filter of the standard: `Agent`, `Verb`, `Activity`,
`Registration`, `RelatedActivities`, `RelatedAgents`, `Since`, `Until`, `Limit`, `Format`, `Ascending` and
`Attachments`. Properties left at their defaults are not sent.

`QueryAllStatementsAsync` follows the `more` links until the last page, one page request at a time.
`QueryStatementsAsync` returns one page as a `StatementResult` (`Statements`, `More`, `ConsistentThrough` and
`Attachments`); pass `More` to `GetMoreStatementsAsync` for the next page. Only the path and query of `More`
are used, and they are sent to the host of `Endpoint`, so credentials never go to another host.

## Documents and ETags

State, Activity Profile and Agent Profile documents are addressed by a key:

| Resource | Key |
| --- | --- |
| State | `new StateKey(activityId, agent, stateId, registration)` (registration is optional) |
| Activity Profile | `new ActivityProfileKey(activityId, profileId)` |
| Agent Profile | `new AgentProfileKey(agent, profileId)` |

`GetDocumentAsync` returns an `XapiDocument` (`Content`, `ContentType`, `ETag`, `LastModified`) or `null` when
there is none. `PutDocumentAsync` replaces a document. Under xAPI 2.0 an LRS answers 409 to a PUT without a
precondition when the document exists, so send `DocumentPrecondition.IfNoneMatchAny` to create and
`DocumentPrecondition.IfMatch(etag)` to replace the version you read. When the document changed in between,
the LRS answers 412 and the client throws `XapiPreconditionFailedException`:

<!-- snippet: client-documents -->
```csharp
var key = new StateKey(course, learner, "bookmark");

var current = await client.GetDocumentAsync(key);
var precondition = current?.ETag is { } etag
    ? DocumentPrecondition.IfMatch(etag)
    : DocumentPrecondition.IfNoneMatchAny;

try
{
    await client.PutDocumentAsync(key, new XapiDocument(Encoding.UTF8.GetBytes("module-3/page-1"), "text/plain"), precondition);
}
catch (XapiPreconditionFailedException)
{
    // Another writer changed the document after it was read: read it again and decide
}
```

`MergeDocumentAsync` sends a POST with a JSON object; the LRS replaces each top-level property of the stored
JSON object with the one sent and creates the document if it does not exist:

<!-- snippet: client-merge -->
```csharp
using var changes = JsonDocument.Parse("""{ "lastPage": 12, "theme": "dark" }""");
var key = new StateKey(course, learner, "preferences");

await client.MergeDocumentAsync(key, changes.RootElement);

var preferences = await client.GetDocumentAsync(key);
var theme = preferences?.ToJson().GetProperty("theme").GetString();
```

`DeleteDocumentAsync` deletes one document (optionally with `IfMatch`), and `DeleteStatesAsync` deletes all
State documents of an Activity and Agent, or of one registration. `GetStateIdsAsync`,
`GetActivityProfileIdsAsync` and `GetAgentProfileIdsAsync` list document ids, optionally only those changed
after `since`.

## Attachments

Attachment data is sent with the statement as `multipart/mixed`. `AttachmentPayload.Create` computes the
SHA-256 of the bytes, and `ToAttachment` builds the Attachment object that refers to it. Asking for
`Attachments = true` in a query returns the data in `StatementResult.Attachments`, matched to the statements
by SHA-2:

<!-- snippet: client-attachments -->
```csharp
var certificate = AttachmentPayload.Create(await File.ReadAllBytesAsync("certificate.pdf"), "application/pdf");
var statement = new Statement(learner, Verbs.Completed, new Activity(course))
{
    Attachments =
    [
        certificate.ToAttachment(
            new Iri("https://example.com/attachment-usages/certificate"),
            LanguageMap.Create("en-US", "Certificate")),
    ],
};

await client.SaveStatementAsync(statement, [certificate]);

var page = await client.QueryStatementsAsync(new StatementQuery { Activity = course, Attachments = true });
foreach (var payload in page.Attachments)
{
    Console.WriteLine($"{payload.Sha2} {payload.ContentType} {payload.Content.Length} bytes");
}
```

With `ValidateBeforeSend`, every Attachment without a `fileUrl` needs a payload with the same SHA-2 (error
code `attachment.payload.missing`), and every payload needs an Attachment (`attachment.payload.unreferenced`,
whose path is the payload's index in the list, such as `attachments[0]`, since payloads are not part of the
JSON). Both codes are constants on `XapiErrorCodes`.
Attachments that only point to a `fileUrl` need no payload. The client integration tests send and receive
attachments against lrsql, and the LRS tests against `Xapi.Lrs`.

`StatementSigner` signs a statement and returns it with its signature attachment and the JWS to send with it;
see [signatures.md](signatures.md).

## Agents, Activities and About

`GetPersonAsync(agent)` returns the Person object the LRS combines from everything it knows about an Agent,
`GetActivityAsync(activityId)` the LRS's definition of an Activity (`null` on 404), and `GetAboutAsync` the
About resource.

## Errors

| Exception | When |
| --- | --- |
| `XapiValidationException` | A statement is invalid for the xAPI version in use and `ValidateBeforeSend` is on. Nothing was sent. `Errors` lists each problem with a JSON path and an error code. Also thrown, as the inner exception of `XapiHttpException`, when a response body is not valid xAPI. |
| `XapiConflictException` | 409: the LRS has a different statement with the same id, or a document PUT had no precondition and the document exists. |
| `XapiPreconditionFailedException` | 412: an `If-Match` or `If-None-Match` precondition failed. |
| `XapiHttpException` | Any other error status, or a successful response the client cannot read. `StatusCode`, `ResponseBody`, `RetryAfter` and `IsTransient` (408, 429, and 5xx except 501 and 505) describe it. The conflict and precondition exceptions derive from it. |
| `NotSupportedException` | Version negotiation found no version both sides support. |
| `HttpRequestException`, `TaskCanceledException` | No response: a network error, or the `HttpClient` timeout. |

<!-- snippet: client-errors -->
```csharp
try
{
    await client.SaveStatementAsync(statement);
}
catch (XapiValidationException exception)
{
    // Nothing was sent: the statement breaks a rule of the xAPI version in use
    foreach (var error in exception.Errors)
    {
        Console.WriteLine($"{error.Path}: {error.Message} ({error.Code})");
    }
}
catch (XapiConflictException)
{
    // The LRS already has a different statement with this id
}
catch (XapiHttpException exception) when (exception.IsTransient)
{
    // 408, 429 or 5xx: try again later, not before exception.RetryAfter if the LRS sent one
}
catch (XapiHttpException exception)
{
    // Any other error status; the LRS usually explains it in the body
    Console.WriteLine($"{(int)exception.StatusCode}: {exception.ResponseBody}");
}
catch (HttpRequestException)
{
    // No response: DNS, connection or TLS failure
}
```

Error messages name the method and path of the request but not its query string, because agent parameters
can hold personal data.
