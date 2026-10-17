# Migrating from TinCan.NET

This guide maps the API of [TinCan.NET](https://github.com/RusticiSoftware/TinCan.NET) (NuGet package
`TinCan`, version 1.3.0, last changed in December 2018) to `Xapi.Core` and `Xapi.Client`. The TinCan.NET names
below were checked against its source at commit `0933bc5`. Forks such as TinCanStandard or Float.TinCan changed
parts of the API (async methods, other constructors); their names may differ from the ones here.

The two libraries are built differently, so a migration is a rewrite of the calls, not a rename:

| | TinCan.NET | xapi-dotnet |
| --- | --- | --- |
| Calls | Synchronous (`HttpWebRequest`) | Asynchronous, with `CancellationToken` (`HttpClient`) |
| Errors | `LRSResponse.success` and `errMsg` | Exceptions with the HTTP status code |
| Models | Mutable classes, lower-case properties | Immutable records, `with` expressions |
| JSON | Newtonsoft.Json, `JObject` in the public API | `System.Text.Json`, no JSON library types except `JsonElement` for extensions and documents |
| Times | `DateTime` | `DateTimeOffset` |
| IRIs | `System.Uri` in many places | `Iri`, compared as exact strings |
| xAPI versions | Up to 1.0.3 | 1.0.3 and 2.0.0 |
| Targets | .NET Framework 3.5 and later | netstandard2.0 (.NET Framework 4.6.2 and later) and net10.0 |
| Validation | None | Before sending, by default |

## Sending a statement

TinCan.NET:

```csharp
var lrs = new RemoteLRS("https://lrs.example.com/xapi/", "key", "secret");

var statement = new Statement
{
    actor = new Agent { mbox = "mailto:learner@example.com", name = "Ada Learner" },
    verb = new Verb { id = new Uri("http://adlnet.gov/expapi/verbs/completed"), display = new LanguageMap() },
    target = new Activity { id = "https://example.com/courses/fire-safety" },
};
statement.verb.display.Add("en-US", "completed");

StatementLRSResponse response = lrs.SaveStatement(statement);
if (response.success)
{
    Console.WriteLine(response.content.id);
}
else
{
    Console.WriteLine(response.errMsg);
}
```

xapi-dotnet:

<!-- snippet: tincan-send -->
```csharp
var client = new XapiClient(http, new XapiClientOptions
{
    Endpoint = new Uri("https://lrs.example.com/xapi/"),
    Credentials = XapiCredentials.Basic("key", "secret"),
    Version = XapiVersion.V1_0_3, // what TinCan.NET sends; leave it unset to negotiate
});

var statement = new Statement(
    Agent.FromMbox("learner@example.com", "Ada Learner"),
    Verbs.Completed,
    new Activity(new Iri("https://example.com/courses/fire-safety")));

try
{
    var id = await client.SaveStatementAsync(statement);
    Console.WriteLine(id);
}
catch (XapiHttpException exception)
{
    Console.WriteLine(exception.Message);
}
```

`XapiClient` does not own the `HttpClient`: create one per LRS and reuse it, or register the client with
`AddXapiClient` (see [client.md](client.md#dependency-injection-and-retries)).

## Querying statements

TinCan.NET:

```csharp
var query = new StatementsQuery
{
    agent = learner,
    verbId = new Uri("http://adlnet.gov/expapi/verbs/completed"),
    since = DateTime.UtcNow.AddDays(-7),
};

StatementsResultLRSResponse result = lrs.QueryStatements(query);
while (result.success)
{
    foreach (var statement in result.content.statements)
    {
        Console.WriteLine(statement.id);
    }

    if (string.IsNullOrEmpty(result.content.more))
    {
        break;
    }

    result = lrs.MoreStatements(result.content);
}
```

xapi-dotnet:

<!-- snippet: tincan-query -->
```csharp
var query = new StatementQuery
{
    Agent = learner,
    Verb = Verbs.Completed.Id,
    Since = DateTimeOffset.UtcNow.AddDays(-7),
};

await foreach (var statement in client.QueryAllStatementsAsync(query))
{
    Console.WriteLine(statement.Id);
}
```

## State documents

TinCan.NET:

```csharp
var activity = new Activity { id = "https://example.com/courses/fire-safety" };
var state = new StateDocument
{
    id = "bookmark",
    activity = activity,
    agent = learner,
    contentType = "text/plain",
    content = Encoding.UTF8.GetBytes("page-3"),
};
lrs.SaveState(state);

StateLRSResponse loaded = lrs.RetrieveState("bookmark", activity, learner);
var bookmark = loaded.content.content == null ? null : Encoding.UTF8.GetString(loaded.content.content);
```

xapi-dotnet:

<!-- snippet: tincan-state -->
```csharp
var key = new StateKey(new Iri("https://example.com/courses/fire-safety"), learner, "bookmark");

await client.PutDocumentAsync(key, new XapiDocument(Encoding.UTF8.GetBytes("page-3"), "text/plain"), DocumentPrecondition.IfNoneMatchAny);

var document = await client.GetDocumentAsync(key);
var bookmark = document is null ? null : Encoding.UTF8.GetString(document.Content.Span);
```

## Client mapping

| TinCan.NET (`RemoteLRS`, `ILRS`) | xapi-dotnet (`XapiClient`) |
| --- | --- |
| `new RemoteLRS(endpoint, username, password)` | `new XapiClient(httpClient, new XapiClientOptions { Endpoint = ..., Credentials = XapiCredentials.Basic(key, secret) })` |
| `RemoteLRS.version`, `TCAPIVersion.V103` | `XapiClientOptions.Version = XapiVersion.V1_0_3`, or `null` to negotiate |
| `RemoteLRS.SetAuth(username, password)`, `RemoteLRS.auth` | `XapiClientOptions.Credentials`: `XapiCredentials.Basic` or `XapiCredentials.Bearer` |
| `RemoteLRS.extended` (extra query parameters on every request) | No equivalent. A `DelegatingHandler` on the `HttpClient` can add them. |
| `About()` | `GetAboutAsync()`; `GetVersionAsync()` for the negotiated version |
| `SaveStatement(statement)` | `SaveStatementAsync(statement)` returns the id |
| `SaveStatements(statements)` | `SaveStatementsAsync(statements)` returns the ids |
| `VoidStatement(id, agent)` | `VoidStatementAsync(id, actor)` |
| `RetrieveStatement(id)` | `GetStatementAsync(id)`, `null` when not found |
| `RetrieveVoidedStatement(id)` | `GetVoidedStatementAsync(id)` |
| `QueryStatements(query)` | `QueryStatementsAsync(query)` for one page, `QueryAllStatementsAsync(query)` for all |
| `MoreStatements(result)` | `GetMoreStatementsAsync(result.More)` |
| `RetrieveStateIds(activity, agent, registration)` | `GetStateIdsAsync(activityId, agent, registration, since)` |
| `RetrieveState(id, activity, agent, registration)` | `GetDocumentAsync(new StateKey(activityId, agent, id, registration))` |
| `SaveState(stateDocument)` | `PutDocumentAsync(stateKey, document, precondition)` |
| `DeleteState(stateDocument)` | `DeleteDocumentAsync(stateKey)` |
| `ClearState(activity, agent, registration)` | `DeleteStatesAsync(activityId, agent, registration)` |
| `RetrieveActivityProfileIds(activity)` | `GetActivityProfileIdsAsync(activityId, since)` |
| `RetrieveActivityProfile(id, activity)` | `GetDocumentAsync(new ActivityProfileKey(activityId, id))` |
| `SaveActivityProfile(document)`, `DeleteActivityProfile(document)` | `PutDocumentAsync` and `DeleteDocumentAsync` with an `ActivityProfileKey` |
| `RetrieveAgentProfileIds(agent)` | `GetAgentProfileIdsAsync(agent, since)` |
| `RetrieveAgentProfile(id, agent)` | `GetDocumentAsync(new AgentProfileKey(agent, id))` |
| `SaveAgentProfile(document)`, `DeleteAgentProfile(document)` | `PutDocumentAsync` and `DeleteDocumentAsync` with an `AgentProfileKey` |
| (none) | `MergeDocumentAsync` (document POST), `GetPersonAsync`, `GetActivityAsync`, attachments, `StatementSigner` |
| `LRSResponse.success`, `errMsg`, `httpException` | Exceptions; see [client.md](client.md#errors) |
| `StatementLRSResponse.content` and the other `content` properties | The return value |

## Model mapping

| TinCan.NET | xapi-dotnet |
| --- | --- |
| `Statement` (`actor`, `verb`, `target`, `result`, `context`, `timestamp`, `id`, `stored`, `authority`, `version`) | `Statement(actor, verb, object)` with `Result`, `Context`, `Timestamp`, `Id`, `Stored`, `Authority`, `Version`, `Attachments`. `target` is `Object`. |
| `Statement.Stamp()` | No equivalent. Set `Id` (for example `UuidV7Generator.Shared.NewGuid()`) and `Timestamp` yourself, or let `StatementOutbox.EnqueueAsync` set both. |
| `Agent { mbox = "mailto:..." }` | `Agent.FromMbox("...")`; `mailto:` is added when missing |
| `Agent { account = new AgentAccount(homePage, name) }` | `Agent.FromAccount(homePage, name)` |
| `Agent { mbox_sha1sum = ... }`, `Agent { openid = ... }` | `new Agent(new MboxSha1Sum(...))`, `new Agent(new OpenId(new Iri(...)))` |
| `Group { member = ... }` | `new Group(members)` for an anonymous Group, `new Group(ifi) { Members = ... }` for an identified one |
| `Verb { id = new Uri(...), display = ... }` | `new Verb(new Iri(...)) { Display = LanguageMap.Create("en-US", ...) }`, or a constant from `Verbs` |
| `Activity { id = "...", definition = ... }` | `new Activity(new Iri("...")) { Definition = new ActivityDefinition { ... } }` |
| `LanguageMap` with `Add(lang, value)` | `LanguageMap.Create(lang, value).With(otherLang, otherValue)` |
| `Extensions` (built from a `JObject`) | `Extensions.Create(iri, jsonElement).With(...)` |
| `Result { duration = TimeSpan }` | `Result { Duration = XapiDuration.FromTimeSpan(...) }` or `XapiDuration.Parse("PT1M30S")` |
| `Score`, `ContextActivities`, `StatementRef`, `SubStatement` | Same names, PascalCase properties |
| `Context` | `Context`, plus `ContextAgents` and `ContextGroups` for xAPI 2.0 |
| `TCAPIVersion` | `XapiVersion` |
| `StatementsQuery` (`agent`, `verbId`, `activityId`, `registration`, `relatedActivities`, `relatedAgents`, `since`, `until`, `limit`, `format`, `ascending`) | `StatementQuery` (`Agent`, `Verb`, `Activity`, `Registration`, `RelatedActivities`, `RelatedAgents`, `Since`, `Until`, `Limit`, `Format`, `Ascending`, `Attachments`) |
| `StatementsQueryResultFormat.IDS`, `EXACT`, `CANONICAL` | `StatementFormat.Ids`, `Exact`, `Canonical` |
| `StatementsResult` (`statements`, `more`) | `StatementResult` (`Statements`, `More`, `ConsistentThrough`, `Attachments`) |
| `StateDocument`, `ActivityProfileDocument`, `AgentProfileDocument` (`id`, `etag`, `timestamp`, `contentType`, `content`) | A key (`StateKey`, `ActivityProfileKey`, `AgentProfileKey`) and an `XapiDocument` (`Content`, `ContentType`, `ETag`, `LastModified`) |
| `x.ToJSON(version)`, `new Statement(new StringOfJSON(json))` | `XapiJson.SerializeStatement(statement, version)`, `XapiJson.ParseStatement(json, version)` |

## Behaviour that changes

**Statements are validated before they are sent.** TinCan.NET sends whatever it is given and leaves the LRS
to reject it. `XapiClient` checks each statement against the rules of the xAPI version in use and throws
`XapiValidationException` without a request. Set `ValidateBeforeSend = false` to turn this off.

**The `version` property is no longer added.** TinCan.NET writes the client's configured version (`1.0.3`)
into every statement it serializes, whatever the statement's own `version` is. xapi-dotnet writes
`Statement.Version` only when it is set, and the LRS fills it in.

**IRIs are kept exactly as written.** TinCan.NET stores verb ids, activity types, account home pages and
extension keys as `System.Uri`, and `Uri.ToString()` can change them: it adds a trailing slash to a bare
host, lower-cases the host and unescapes `%xx` sequences. LRSs compare IRIs as strings, so if your existing
data was written through TinCan.NET, check that the IRIs you now send match what is stored.

**Timestamps keep their offset.** TinCan.NET reads timestamps into `DateTime`, converting to local time, and
writes a `DateTime` with `Kind=Unspecified` without an offset, which xAPI 2.0 rejects. xapi-dotnet uses
`DateTimeOffset` and writes UTC.

**A missing document is `null`.** TinCan.NET reports a document that does not exist as a successful response
with empty content. `GetDocumentAsync` returns `null`.

**Document writes can use preconditions.** TinCan.NET never sends `If-Match` or `If-None-Match`. Against an
xAPI 2.0 LRS a PUT without them fails with 409 when the document exists, so pass
`DocumentPrecondition.IfMatch(etag)` or `IfNoneMatchAny`. With `Version = XapiVersion.V1_0_3`, State PUTs
without a precondition still work as before; Activity and Agent Profile PUTs need one in 1.0.3 too.

**Query booleans are sent as `true` and `false`.** TinCan.NET sends `True` and `False`, which some LRSs reject.

## What xapi-dotnet does not have

- A synchronous API. Call the async methods from async code. In older code bases that cannot be made async,
  blocking on a task (`.GetAwaiter().GetResult()`) works outside a synchronization context, but it can
  deadlock under one, for example in WinForms, WPF or classic ASP.NET request threads.
- Targets below .NET Framework 4.6.2. TinCan.NET supports .NET Framework 3.5.
- The `extended` query parameters and the raw `auth` header string of `RemoteLRS`. Use a `DelegatingHandler`.
- Newtonsoft.Json types. Code that passes `JObject` around has to convert, for example by serializing to a
  string and parsing it with `System.Text.Json`.
- Tested Unity WebGL support. The packages pass their tests in Unity IL2CPP players, but not yet in WebGL
  ([unity.md](unity.md)).

xapi-dotnet adds xAPI 2.0, attachments (`multipart/mixed`), signed statements
([signatures.md](signatures.md)), document merge (POST), the Agents and Activities
resources, ETag preconditions, `since` on document id lists, and a durable [outbox](outbox.md) for offline
and unreliable networks, which TinCan.NET's maintainers declined to add to the library
([TinCan.NET issue #13](https://github.com/RusticiSoftware/TinCan.NET/issues/13)).
