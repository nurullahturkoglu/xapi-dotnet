# Getting started

This guide installs the packages, sends a statement to an LRS, and hosts an LRS inside an ASP.NET Core app.
Each section links to the guide that covers its topic in full.

## Install

| You want to | Package |
| --- | --- |
| Read, write or validate xAPI JSON | `Xapi.Core` |
| Send statements and documents to any LRS | `Xapi.Client` (brings `Xapi.Core`) |
| Queue statements on disk and deliver them when the LRS can be reached | `Xapi.Client.Outbox` (brings `Xapi.Client`) |
| Run an LRS in an ASP.NET Core app | `Xapi.Lrs.Sqlite` or `Xapi.Lrs.PostgreSql` (bring `Xapi.Lrs`) |

`Xapi.Core`, `Xapi.Client` and `Xapi.Client.Outbox` target netstandard2.0 (.NET Framework 4.6.2 and later) and
net10.0. The LRS packages target net10.0.

The packages are not on NuGet yet. Until the first preview is published, build them from source and use the
output folder as a package source:

```bash
git clone https://github.com/nurullahturkoglu/xapi-dotnet.git
cd xapi-dotnet
dotnet pack xapi-dotnet.slnx -c Release -o artifacts/packages
```

Then, in your project:

```bash
dotnet nuget add source /path/to/xapi-dotnet/artifacts/packages --name xapi-dotnet-local
dotnet add package Xapi.Client --prerelease
```

Package versions come from git tags, and the repository has none yet, so the packages are versioned
`0.0.0-preview.0.N`. Once a preview is on NuGet, `dotnet add package Xapi.Client --prerelease` is enough.

## Send a statement

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

`Endpoint` is the base address of the LRS; resource names such as `statements` are appended to it. No xAPI
version is set, so before its first request the client reads the LRS's About resource and uses 2.0.0 if the
LRS supports it, otherwise 1.0.3. The statement is validated against the rules of that version before it is
sent, and an invalid one throws `XapiValidationException` without a request.

Reading it back:

<!-- snippet: read-statement -->
```csharp
var stored = await client.GetStatementAsync(id);
if (stored is not null)
{
    Console.WriteLine(XapiJson.SerializeStatement(stored, await client.GetVersionAsync()));
}
```

The [client guide](client.md) covers queries, documents, attachments, retries and errors.

## Send from an ASP.NET Core app

In a web app, register the client with dependency injection and put an outbox in front of it, so a request
handler only writes the statement to disk and an LRS outage does not fail the request. With `using Xapi;`,
`using Xapi.Client;` and `using Xapi.Client.Outbox;`:

<!-- snippet: client-di-outbox -->
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddXapiClient(options =>
    {
        options.Endpoint = new Uri("https://lrs.example.com/xapi/");
        options.Credentials = XapiCredentials.Basic(builder.Configuration["Xapi:Key"]!, builder.Configuration["Xapi:Secret"]!);
    })
    .AddOutbox(Path.Combine(builder.Environment.ContentRootPath, "xapi-outbox"));

var app = builder.Build();

app.MapPost("/courses/{courseId}/complete", async (string courseId, string email, StatementOutbox outbox, CancellationToken cancellationToken) =>
{
    var statement = new Statement(
        Agent.FromMbox(email),
        Verbs.Completed,
        new Activity(new Iri($"https://courses.example.com/{courseId}")));

    await outbox.EnqueueAsync(statement, cancellationToken);
    return Results.Accepted();
});

app.Run();
```

`AddXapiClient` registers `XapiClient` with `IHttpClientFactory`. `AddOutbox` registers a
`StatementOutbox` that keeps its queue in the given folder and a hosted service that delivers it in the
background, with retries, backoff and dead letters. `EnqueueAsync` gives the statement an id before storing
it, so a statement that is sent twice after a crash is stored once. The [outbox guide](outbox.md) has the
details.

## Read and validate JSON without an LRS

`Xapi.Core` works on its own, for example to check statements before they are stored elsewhere. With
`using Xapi;`, `using Xapi.Json;` and `using Xapi.Validation;`:

<!-- snippet: core-json -->
```csharp
var parsed = XapiJson.ParseStatement(json, XapiVersion.V2_0_0);
if (!parsed.IsSuccess)
{
    foreach (var error in parsed.Errors)
    {
        Console.WriteLine(error); // "<JSON path>: <message> (<error code>)"
    }

    return;
}

var statement = parsed.Value with { Timestamp = DateTimeOffset.UtcNow };
var errors = StatementValidator.Validate(statement, XapiVersion.V1_0_3);
Console.WriteLine(errors.Count == 0
    ? XapiJson.SerializeStatement(statement, XapiVersion.V1_0_3)
    : "Not valid as xAPI 1.0.3: " + string.Join("; ", errors));
```

Every call takes the xAPI version whose rules apply; [versions.md](versions.md) lists the differences.

## Host an LRS

Create a web app and add the SQLite provider:

```bash
dotnet new web -n MyLrs
cd MyLrs
dotnet add package Xapi.Lrs.Sqlite --prerelease
```

Replace `Program.cs` with this (`using Xapi.Lrs;` at the top):

<!-- snippet: host-lrs -->
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddXapiLrs(options =>
    {
        options.MigrateOnStartup = true;
        options.Credentials.Add("my-app", builder.Configuration["Lrs:Secret"]!, XapiScope.All);
    })
    .UseSqlite("Data Source=lrs.db");

var app = builder.Build();
app.MapXapiLrs("/xapi");
app.Run();
```

`MigrateOnStartup` creates the database on the first run. The credential is a Basic key and secret with every
scope; the secret is read from configuration. To try it, pass the secret on the command line and send a
statement with curl, using the address that `dotnet run` prints:

```bash
dotnet run -- --Lrs:Secret=change-me

URL=http://localhost:5000
curl $URL/xapi/about
curl -u my-app:change-me -H "X-Experience-API-Version: 2.0.0" -H "Content-Type: application/json" \
  -d '{"actor":{"mbox":"mailto:learner@example.com"},"verb":{"id":"http://adlnet.gov/expapi/verbs/completed"},"object":{"id":"https://example.com/courses/fire-safety"}}' \
  $URL/xapi/statements
curl -u my-app:change-me -H "X-Experience-API-Version: 2.0.0" "$URL/xapi/statements?verb=http://adlnet.gov/expapi/verbs/completed"
```

The POST answers with the id of the stored statement. The [LRS guide](lrs.md) covers credentials and scopes,
delegated authentication, PostgreSQL, migrations and limits.

## Test your code against a real LRS

The embedded LRS also runs inside a test process, without Docker or a network port, so code that uses
`XapiClient` can be tested against real LRS behaviour. [samples/FakeLrsInTests](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/samples/FakeLrsInTests)
shows how.

## Samples

| Sample | Shows |
| --- | --- |
| [ConsoleClient](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/samples/ConsoleClient) | Sending a statement, reading it back, and delivering one through a file outbox. Published with Native AOT in CI. |
| [EmbedInAspNetCore](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/samples/EmbedInAspNetCore) | An app with its own endpoints and an LRS under `/xapi`, with credentials from configuration. |
| [FakeLrsInTests](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/samples/FakeLrsInTests) | xUnit tests of an app's xAPI code against the embedded LRS hosted in memory. |
| [LrsHost](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/samples/LrsHost) | A standalone LRS on SQLite or PostgreSQL; the target of the conformance runs. |
