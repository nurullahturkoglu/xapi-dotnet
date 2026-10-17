# xapi-dotnet

[![CI](https://github.com/nurullahturkoglu/xapi-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/nurullahturkoglu/xapi-dotnet/actions/workflows/ci.yml)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/LICENSE)

Experience API (xAPI 2.0 / IEEE 9274.1.1, and 1.0.3) for .NET: models and validation, an async LRS client
with a durable outbox, and a Learning Record Store you can embed in an ASP.NET Core app.

**Status: preview.** `0.1.0-preview.1` is on [NuGet](https://www.nuget.org/packages?q=Xapi.&prerelease=true) and
the LRS image is on [ghcr.io](https://github.com/nurullahturkoglu/xapi-dotnet/pkgs/container/xapi-lrs). The API can
change between previews; the [1.0 milestone](https://github.com/nurullahturkoglu/xapi-dotnet/milestone/2) lists
what is left. Documentation: https://nurullahturkoglu.github.io/xapi-dotnet/

The 2.0 standard is IEEE 9274.1.1-2023, also adopted as ISO/IEC/IEEE 39274-1-1:2025.

## Packages

| Package | What it is | Targets |
| --- | --- | --- |
| `Xapi.Core` | Statement model, strict JSON reading and writing for 1.0.3 and 2.0.0, validation, statement comparison | netstandard2.0, net10.0 |
| `Xapi.Client` | Async client for every LRS resource, version negotiation, attachments, statement signing | netstandard2.0, net10.0 |
| `Xapi.Client.Outbox` | Durable outbox: retries, error classification, dead letters, idempotent resend | netstandard2.0, net10.0 |
| `Xapi.Lrs` | `AddXapiLrs` / `MapXapiLrs`: an LRS inside your ASP.NET Core app | net10.0 |
| `Xapi.Lrs.Sqlite` | SQLite storage and migrations for `Xapi.Lrs` | net10.0 |
| `Xapi.Lrs.PostgreSql` | PostgreSQL storage and migrations for `Xapi.Lrs` | net10.0 |

The `netstandard2.0` builds are for .NET Framework 4.6.2 and later and for Unity. JSON is read and written by
hand with `Utf8JsonReader` and `Utf8JsonWriter`, without reflection, so the `net10.0` builds of the Core,
Client and Outbox packages can be trimmed and published with Native AOT; CI publishes
`samples/ConsoleClient` that way and fails on any warning. Under Unity they pass their tests in the editor, in
IL2CPP players and in WebGL, where requests go through `UnityWebRequestHandler` from the Unity package in
`src/Xapi.Unity` ([docs/unity.md](docs/unity.md)). Android and iOS are not tested yet.

## Quick start: send a statement

```bash
dotnet add package Xapi.Client --prerelease
```

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
To keep statements through network outages and restarts, put a
[`StatementOutbox`](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/outbox.md) in front of the client; in an ASP.NET Core app, register both with
`AddXapiClient(...).AddOutbox(...)` ([example](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/getting-started.md#send-from-an-aspnet-core-app)).

## Quick start: host an LRS

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
`https://<your app>/xapi/` as their endpoint, with the key `my-app` and that secret. The [LRS guide](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/lrs.md) covers scopes, delegated authentication, PostgreSQL, migrations
and limits.

## Conformance

The LRS passes all tests of the
[ADL LRS Conformance Test Suite](https://github.com/adlnet/lrs-conformance-test-suite) at commit `5bc232d`:
1,365 of 1,365 for xAPI 1.0.3 and 1,435 of 1,435 for 2.0.0, on SQLite and on PostgreSQL (as of commit
`69bb8ff`, 17 October 2026). Both batteries run against both databases in CI on every pull request, and the
gate fails on any failing or skipped test.

In two places the suite expects more than IEEE 9274.1.1 requires, and the LRS follows the suite.
[docs/conformance.md](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/conformance.md) explains both, and how the gate works.

## Why another xAPI library

TinCan.NET, the library most .NET projects use today, has not changed since 2018: it targets `net35`,
its calls block, it exposes Newtonsoft.Json types, and it has no xAPI 2.0 support. Teams that need
durable sending end up writing their own outbox and retry logic. This project aims to be one maintained
set of packages for both sides, tested against the conformance suite. The
[migration guide](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/migrating-from-tincan.md) maps TinCan.NET calls and types to this library.

## Documentation

- [Getting started](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/getting-started.md): install, send a statement, host an LRS
- [Client](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/client.md): versions, credentials, dependency injection and retries, documents, attachments, errors
- [Outbox](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/outbox.md): durable delivery, dead letters, storage
- [LRS](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/lrs.md): options, storage providers, migrations, authentication, limits
- [Signed statements](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/signatures.md)
- [xAPI versions](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/versions.md): 1.0.3 and 2.0.0 differences and how the packages handle them
- [Migrating from TinCan.NET](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/migrating-from-tincan.md)
- [Conformance](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/conformance.md)
- [Samples](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/samples): a console client, an LRS
  embedded in an existing app, tests against an in-memory LRS, and a standalone LRS

The design decisions are recorded as
[architecture decision records](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/docs/adr).

The [Documentation site](https://nurullahturkoglu.github.io/xapi-dotnet/) has these guides and the API
reference, built from `main`.

## Status and versioning

Versions follow [Semantic Versioning](https://semver.org). During 0.x, minor versions can contain breaking
changes; [CHANGELOG.md](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/CHANGELOG.md) lists them under "Breaking". Previews are published as
`-preview.N`. The storage interfaces of `Xapi.Lrs` are marked experimental (`XAPI0001`): they are not covered
by Semantic Versioning, also after 1.0, and may change in any release until they are declared stable (see
[Experimental storage API](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/docs/lrs.md#experimental-storage-api)).
Version 1.0.0 requires both conformance batteries to pass on both databases. What is planned for each version
is tracked in the [milestones](https://github.com/nurullahturkoglu/xapi-dotnet/milestones).

## Contributing, security, license

Contributions are welcome. Please open an issue before starting on anything larger than a small fix; see
[CONTRIBUTING.md](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/CONTRIBUTING.md). Report security problems privately as described in
[SECURITY.md](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/SECURITY.md).

Licensed under the [Apache License 2.0](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/LICENSE).
Third-party material is listed in
[THIRD-PARTY-NOTICES.md](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/THIRD-PARTY-NOTICES.md).

xAPI (Experience API) is a specification created by the Advanced Distributed Learning (ADL) Initiative and published as IEEE 9274.1.1-2023. "Tin Can API" is a former name of the specification. This project is an independent implementation and is not affiliated with, endorsed by, or certified by ADL, the U.S. Department of Defense, IEEE, or Rustici Software. IEEE is a registered trademark of The Institute of Electrical and Electronics Engineers, Inc.
