# xapi-dotnet

Experience API (xAPI 2.0 / IEEE 9274.1.1, and 1.0.3) for .NET: models and validation, an async LRS client
with a durable outbox, and a Learning Record Store you can embed in an ASP.NET Core app.

**Status: preview.** `0.1.0-preview.1` is on [NuGet](https://www.nuget.org/packages?q=Xapi.&prerelease=true) and
the LRS image is on [ghcr.io](https://github.com/nurullahturkoglu/xapi-dotnet/pkgs/container/xapi-lrs). The API can
change between previews; the [1.0 milestone](https://github.com/nurullahturkoglu/xapi-dotnet/milestone/2) lists
what is left.

This site is built from the `main` branch. The source, issues and the changelog are on
[GitHub](https://github.com/nurullahturkoglu/xapi-dotnet).

## Packages

| Package | What it is | Targets |
| --- | --- | --- |
| `Xapi.Core` | Statement model, strict JSON reading and writing for 1.0.3 and 2.0.0, validation, statement comparison | netstandard2.0, net10.0 |
| `Xapi.Client` | Async client for every LRS resource, version negotiation, attachments, statement signing | netstandard2.0, net10.0 |
| `Xapi.Client.Outbox` | Durable outbox: retries, error classification, dead letters, idempotent resend | netstandard2.0, net10.0 |
| `Xapi.Lrs` | `AddXapiLrs` / `MapXapiLrs`: an LRS inside your ASP.NET Core app | net10.0 |
| `Xapi.Lrs.Sqlite` | SQLite storage and migrations for `Xapi.Lrs` | net10.0 |
| `Xapi.Lrs.PostgreSql` | PostgreSQL storage and migrations for `Xapi.Lrs` | net10.0 |

The [API reference](xref:Xapi) is generated from the XML documentation of the `net10.0` builds.

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

The client asks the LRS which xAPI version to use, validates the statement for that version and sends it.
The [client guide](client.md) covers the rest of the client, and the [outbox guide](outbox.md) keeps
statements through network outages and restarts.

## Host an LRS

In an ASP.NET Core app with the `Xapi.Lrs.Sqlite` package:

<!-- snippet: host-lrs -->
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddXapiLrs(builder.Configuration.GetSection("Lrs"), options => options.MigrateOnStartup = true)
    .UseSqlite("Data Source=lrs.db");

var app = builder.Build();
app.MapXapiLrs("/xapi");
app.Run();
```

`appsettings.json` lists the credentials under `Lrs`, without their secrets:

```json
{
  "Lrs": {
    "Credentials": [ { "Key": "my-app", "Scopes": [ "all" ] } ]
  }
}
```

The secret comes from user secrets or the environment (`Lrs__Credentials__0__Secret`). Clients then use
`https://<your app>/xapi/` as their endpoint, with the key `my-app` and that secret. The [LRS guide](lrs.md) covers scopes, delegated authentication, PostgreSQL, migrations and
limits. The LRS passes the full ADL LRS Conformance Test Suite for both versions on both databases; see
[Conformance](conformance.md).

## Guides

- [Getting started](getting-started.md): install, send a statement, host an LRS
- [Client](client.md): versions, credentials, dependency injection and retries, documents, attachments, errors
- [Outbox](outbox.md): durable delivery, dead letters, storage
- [LRS](lrs.md): options, storage providers, migrations, authentication, limits
- [Signed statements](signatures.md)
- [xAPI versions](versions.md): 1.0.3 and 2.0.0 differences and how the packages handle them
- [Migrating from TinCan.NET](migrating-from-tincan.md)
- [Conformance](conformance.md)

The design decisions are recorded as [architecture decision records](adr/0001-product-shape.md).

xAPI (Experience API) is a specification created by the Advanced Distributed Learning (ADL) Initiative and
published as IEEE 9274.1.1-2023. This project is an independent implementation and is not affiliated with,
endorsed by, or certified by ADL, the U.S. Department of Defense, IEEE, or Rustici Software. IEEE is a
registered trademark of The Institute of Electrical and Electronics Engineers, Inc.
