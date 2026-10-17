# Xapi.Client

Experience API (xAPI 2.0, IEEE 9274.1.1, and 1.0.3) client for .NET over `HttpClient`. It covers every LRS
resource: Statements with attachments and signing, State and profile documents with ETag preconditions, Agents,
Activities and About. The client negotiates the xAPI version with the LRS and validates statements before it
sends them.

```bash
dotnet add package Xapi.Client
```

## Usage

With `using Xapi;` and `using Xapi.Client;`:

<!-- snippet: send-statement -->
```csharp
using var http = new HttpClient();
var client = new XapiClient(http, new XapiClientOptions
{
    Endpoint = new Uri("https://lrs.example.com/xapi/"),
    Credentials = XapiCredentials.Basic(key, secret),
});

var statement = new Statement(
    Agent.FromMbox("learner@example.com", "Ada Learner"),
    Verbs.Completed,
    new Activity(new Iri("https://example.com/courses/fire-safety")));

var id = await client.SaveStatementAsync(statement);
Console.WriteLine($"The LRS stored statement {id}");
```

To keep statements through network outages and restarts, add `Xapi.Client.Outbox`.

## Target frameworks

`netstandard2.0` (.NET Framework 4.6.2 and later) and `net10.0`. The `net10.0` build can be trimmed and
published with Native AOT, and adds registration with dependency injection and `IHttpClientFactory`.

## Links

- [Client guide](https://nurullahturkoglu.github.io/xapi-dotnet/client.html)
- [Getting started](https://nurullahturkoglu.github.io/xapi-dotnet/getting-started.html)
- [API reference](https://nurullahturkoglu.github.io/xapi-dotnet/api/Xapi.Client.html)
- [Source, issues and changelog](https://github.com/nurullahturkoglu/xapi-dotnet)

## License

Apache License 2.0. The package contains the `LICENSE`, `NOTICE` and `THIRD-PARTY-NOTICES.md` files.
