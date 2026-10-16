# xapi-dotnet

[![CI](https://github.com/nurullahturkoglu/xapi-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/nurullahturkoglu/xapi-dotnet/actions/workflows/ci.yml)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/LICENSE)

Experience API (xAPI 2.0 / IEEE 9274.1.1, and 1.0.3) for .NET: models and validation, an async LRS client
with a durable outbox, and a Learning Record Store you can embed in an ASP.NET Core app.

**Status: early development.** Nothing is published on NuGet yet. `Xapi.Core` is being written; the other
packages are planned. The first preview (`0.1.0-preview.1`, Core and Client) is the next milestone.
Until then, the packages below describe the design, not something you can install.

The 2.0 standard is IEEE 9274.1.1-2023, also adopted as ISO/IEC/IEEE 39274-1-1:2025.

## Packages

| Package | What it is | Targets | Status |
| --- | --- | --- | --- |
| `Xapi.Core` | Statement model, strict JSON reading and writing for 1.0.3 and 2.0.0, validation, statement comparison | netstandard2.0, net10.0 | In development |
| `Xapi.Client` | Async client for every LRS resource, version negotiation, attachments | netstandard2.0, net10.0 | Planned |
| `Xapi.Client.Outbox` | Durable outbox: retries, error classification, dead letters, idempotent resend | netstandard2.0, net10.0 | Planned |
| `Xapi.Lrs` | `AddXapiLrs` / `MapXapiLrs`: an LRS inside your ASP.NET Core app | net10.0 | Planned |
| `Xapi.Lrs.Sqlite` | SQLite storage and migrations for `Xapi.Lrs` | net10.0 | Planned |
| `Xapi.Lrs.PostgreSql` | PostgreSQL storage and migrations for `Xapi.Lrs` | net10.0 | Planned |

The `netstandard2.0` builds are for .NET Framework 4.6.2 and later and for Unity. JSON is read and written by hand with
`Utf8JsonReader` and `Utf8JsonWriter`, without reflection, so that the `net10.0` builds can be trimmed and
published with NativeAOT; CI will check this with an AOT publish once there is a sample to publish. Unity
support will only be claimed after it has been tested on IL2CPP and WebGL.

## Quick start

Quick start code will be added here with the first preview, once it compiles against a published package.

## Conformance

The LRS will be measured with the [ADL LRS Conformance Test Suite](https://github.com/adlnet/lrs-conformance-test-suite)
(commit `5bc232d`): both batteries, on SQLite and PostgreSQL, in CI on every pull request. The suite has
1,365 tests for xAPI 1.0.3 and 1,435 tests for 2.0.0.

Results for this project are coming: the LRS does not exist yet. This section will list the suite commit,
our commit, the number of passing tests per battery and database, and a link to the CI run. Known issues
in the suite, and how this project handles them, will be listed here too.

## Why another xAPI library

TinCan.NET, the library most .NET projects use today, has not changed since 2018: it targets `net35`,
its calls block, it exposes Newtonsoft.Json types, and it has no xAPI 2.0 support. Teams that need
durable sending end up writing their own outbox and retry logic. This project aims to be one maintained
set of packages for both sides, tested against the conformance suite. A migration guide from TinCan.NET
is planned.

## Documentation

Guides are planned for getting started, the client, the outbox, the LRS, xAPI versions, migrating from
TinCan.NET, and the conformance report. The design decisions are recorded as
[architecture decision records](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/docs/adr).

## Status and versioning

Versions follow [Semantic Versioning](https://semver.org). During 0.x, minor versions can contain breaking
changes; [CHANGELOG.md](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/CHANGELOG.md) lists them
under "Breaking". Previews are published as `-preview.N`. Version 1.0.0 requires both conformance batteries
to pass on both databases.

## Contributing, security, license

Contributions are welcome. Please open an issue before starting on anything larger than a small fix; see
[CONTRIBUTING.md](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/CONTRIBUTING.md). Report
security problems privately as described in
[SECURITY.md](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/SECURITY.md).

Licensed under the [Apache License 2.0](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/LICENSE).
Third-party material is listed in
[THIRD-PARTY-NOTICES.md](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/THIRD-PARTY-NOTICES.md).

xAPI (Experience API) is a specification created by the Advanced Distributed Learning (ADL) Initiative and published as IEEE 9274.1.1-2023. "Tin Can API" is a former name of the specification. This project is an independent implementation and is not affiliated with, endorsed by, or certified by ADL, the U.S. Department of Defense, IEEE, or Rustici Software. IEEE is a registered trademark of The Institute of Electrical and Electronics Engineers, Inc.
