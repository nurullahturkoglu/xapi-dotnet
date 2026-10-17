# Contributing

Thanks for your interest. This file covers how to build and test the code, how changes are proposed, and
how releases are made.

Please open an issue before starting on anything larger than a small fix, so we can agree on the approach
first. Questions are welcome in Discussions. Security problems go through [SECURITY.md](SECURITY.md), not
public issues. Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Prerequisites

- .NET 10 SDK 10.0.4xx (10.0.402 or a later patch). `global.json` pins the feature band so local builds run the same analyzers as CI.
- Docker, for the conformance suite and the PostgreSQL tests.
- Node.js 22, for the conformance suite.
- Git with full history. Package versions come from git tags (MinVer), so a shallow clone builds as `0.0.0-preview.0`.

Building the `net481` test target needs Windows. On macOS and Linux the test projects target `net10.0` only.

## Build and test

```bash
dotnet build
dotnet test
```

Both commands pick up `xapi-dotnet.slnx` in the repository root. Tests run on Microsoft.Testing.Platform
(see `global.json`), so options go straight to `dotnet test`:

```bash
dotnet test --project tests/Xapi.Core.Tests
dotnet test --project tests/Xapi.Core.Tests --filter-class "*StatementTests"
dotnet test --solution xapi-dotnet.slnx -c Release --coverage --coverage-output-format cobertura
```

`tests/Xapi.Lrs.Tests` hosts `samples/LrsHost` in memory and runs the storage tests against SQLite and
against PostgreSQL in a Testcontainers container. The PostgreSQL rows run on Linux, where CI has Docker, and
are skipped elsewhere. Set `XAPI_POSTGRES_TESTS=1` to run them on any machine with Docker, or `0` to skip
them.

Warnings are errors, in local builds as well as CI. Code style comes from `.editorconfig` and is checked
during the build.

## Client integration tests

`tests/Xapi.Client.IntegrationTests` runs `XapiClient` against lrsql in Docker (the image pinned in
`eng/conformance/cts.env`), for xAPI 1.0.3 and 2.0.0. It builds with the solution but is not run by the
plain `dotnet test` above, because the macOS and Windows CI runners cannot run Linux containers. CI runs
it on Linux in the `client-integration` job. Locally, with Docker running:

```bash
dotnet test --project tests/Xapi.Client.IntegrationTests -c Release -p:XapiIntegrationTests=true
```

Without `-p:XapiIntegrationTests=true` that command fails instead of passing with nothing run. The lrsql
image is linux/amd64 only, so on Apple silicon it runs under emulation and takes a little longer to start.

## Conformance suite

The ADL LRS Conformance Test Suite runs in CI on every pull request. To run the same battery locally:

```bash
eng/conformance/run-local.sh --target lrsql --xapi 2.0.0                  # reference run against lrsql
eng/conformance/run-local.sh --target local --db sqlite --xapi 2.0.0      # our LRS, started first
```

For `--target local`, start the sample LRS first with `dotnet run --project samples/LrsHost`. It listens on
`http://localhost:8080/xapi` with the credentials the script expects and stores data in `lrs.db` in the
working directory. For PostgreSQL, pass `--Lrs:Database=postgres --Lrs:ConnectionString=<connection
string>` after `--`, and use `--db postgres` so the run is gated with the PostgreSQL floor.

`eng/conformance/run-local.sh --help` lists the options. The suite is pinned to a commit in
`eng/conformance/cts.env`, and the gate fails if the number of passing tests drops below
`eng/conformance/floor.json`. The floor only goes up. A known failure may be added to
`eng/conformance/known-failures-<version>.txt` only with a link to an upstream suite issue.

## Database migrations

The EF Core model is in `src/Xapi.Lrs/Storage`; each provider package carries its own migrations. After
changing the model, add a migration to both with `eng/ef-migrations.sh add <Name>` (it uses the `dotnet-ef`
version pinned in `.config/dotnet-tools.json`), and remove the last one with `eng/ef-migrations.sh remove`.

## Public API snapshots

Every shipped assembly has an approved snapshot of its public API in
`tests/Xapi.PublicApi.Tests/ApiSnapshots/<Assembly>.approved.txt`, taken from the `net10.0` build. The
`netstandard2.0` builds of `Xapi.Core`, `Xapi.Client` and `Xapi.Client.Outbox` have their own,
`<Assembly>.netstandard2.0.approved.txt`. If your change adds, removes or changes a public member, the test
`Public_API_matches_the_approved_snapshot` (or `NetStandard_public_API_matches_the_approved_snapshot`) fails
and writes `<Assembly>.received.txt` (or `<Assembly>.netstandard2.0.received.txt`) next to the approved file.
To accept the change:

1. Run `dotnet test --project tests/Xapi.PublicApi.Tests`.
2. Compare the received and approved files and check that every difference is intended.
3. Copy the received file over the approved file and commit it with your change.

The approved file is part of the diff, so reviewers see API changes directly. Do not commit
`*.received.txt` files. When a new package is added, add a row for its assembly to `PublicApiTests`; the
first run fails and writes the received file, which you review and rename to `.approved.txt`. A package that
also targets `netstandard2.0` needs a row in the `netstandard2.0` test too, and a `netstandard2.0` project
reference in `Xapi.PublicApi.Tests.csproj`.

## Package tests

`tests/Xapi.PackageTests` consumes the packed nupkgs from `artifacts/packages` instead of referencing the
projects. It checks what ends up in the package (README, notices, target frameworks, dependency versions,
XML docs, strong name). It is not part of the solution, because it can only restore after packing:

```bash
dotnet pack xapi-dotnet.slnx -c Release -o artifacts/packages
dotnet test --project tests/Xapi.PackageTests -c Release -p:XapiPackageVersion=<version>
```

`<version>` is the version in the nupkg file name, for example `0.0.0-preview.0` in
`Xapi.Core.0.0.0-preview.0.nupkg`. The project restores into `artifacts/package-tests/packages`, so delete
`artifacts/` before packing the same version again.

Each package ships its own `README.md` from its project folder (`src/<Package>/README.md`), which nuget.org
shows on the package page. Keep it short, and mark its C# blocks as snippets like the guides (see
`tests/Xapi.Documentation.Tests`).

## Dependency floors

On `netstandard2.0`, `Xapi.Core`, `Xapi.Client` and `Xapi.Client.Outbox` depend on System.Text.Json,
Microsoft.Bcl.AsyncInterfaces, Microsoft.Bcl.TimeProvider and System.Diagnostics.DiagnosticSource. The versions
in `Directory.Packages.props` become minimum versions in the nuspec, and a .NET Framework or Unity app that pins
an older one would have to upgrade. So each is a floor: the lowest version that has every API the libraries use
and no known vulnerability, not the newest. On `net10.0` these come with the shared framework.

To check a candidate, set it in `Directory.Packages.props` and run `dotnet build`: the `netstandard2.0` builds
fail on an API the version lacks, and NuGet audit fails restore on a version with a known vulnerability. Raise a
floor only when the code needs a newer API or an advisory covers the floor, and then also change the comment
next to it and `DependencyFloors` in `tests/Xapi.PackageTests/PackageUnderTest.cs`. Dependabot ignores these
four packages so that floors do not move with routine updates.

The `net481` test targets restore against the floors, so the .NET Framework runs test the oldest versions the
packages allow. For that reason they do not reference the code coverage extension, which needs newer versions.

## Documentation site

The site at https://nurullahturkoglu.github.io/xapi-dotnet/ is built with docfx from the guides in `docs/`
and the XML documentation of the `net10.0` builds in `src/`. The configuration is `docs/docfx.json`, the
navigation is in `docs/toc.yml` and `docs/adr/toc.yml`, and `docs/index.md` is the home page. The `Docs`
workflow builds it on every pull request and deploys it from `main`. To build and preview it locally:

```bash
dotnet tool restore
dotnet docfx docs/docfx.json --warningsAsErrors --serve
```

The site is written to `artifacts/docs-site` and served at http://localhost:8080. Leave out `--serve` to
only build it. Broken links and unresolved `cref`s are warnings, and the workflow fails on any warning.
Links from a guide to files outside `docs/` (samples, `eng/`) must be absolute GitHub URLs, because the
site only contains `docs/`. A new guide also needs an entry in `docs/toc.yml`.

## Coding conventions

- One public type per file. Every public member has XML documentation (missing docs fail the build).
- Comments explain why, not what. Cite the standard as `/// <remarks>IEEE 9274.1.1, 4.2.2.1</remarks>`.
- Library code uses `ConfigureAwait(false)`.
- Tests use xUnit v3. Test names describe behaviour with underscores, for example
  `Parse_rejects_unknown_property`. There is no mocking library; fake an `HttpMessageHandler` by hand.
- Text taken from the IEEE xAPI documentation or derived from the conformance suite must be attributed as
  described in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Branches, commits and pull requests

Branch names start with `feat/`, `fix/`, `test/`, `docs/`, `ci/` or `chore/`, for example
`feat/statement-equivalence`.

Commit messages are plain English sentences in the imperative mood, with a subject line of at most 72
characters: "Add statement equivalence comparer". If the reason for the change is not obvious, explain it
in the body.

Each pull request links its issue (`Closes #12`) and covers one change. Pull requests are squash-merged,
and the pull request title becomes the commit message, so write it the same way. Before asking for review:

- `dotnet build` and `dotnet test` pass.
- New behaviour has tests. For a bug fix, the test fails without the fix.
- The public API snapshot is updated if the public API changed.
- `CHANGELOG.md` has an entry under `[Unreleased]` if users will notice the change.

## Releasing

Releases are made by the maintainer. The version comes from the git tag through MinVer; no file holds it.

1. In a pull request, rename `[Unreleased]` in `CHANGELOG.md` to `[x.y.z] - YYYY-MM-DD`, add a new empty
   `[Unreleased]` section and the comparison links, and record measured results (test counts, conformance
   results, benchmarks where relevant). Merge it.
2. Tag the merge commit and push the tag: `git tag vX.Y.Z && git push origin vX.Y.Z`. Previews use
   `vX.Y.Z-preview.N`.
3. The `Release` workflow builds, tests and packs, checks that the package version matches the tag and that
   `CHANGELOG.md` has a section for it, and attests build provenance for the nupkgs. It then waits for
   approval of the `nuget` environment.
4. Review the run and approve the `nuget` environment in GitHub. The workflow then publishes to nuget.org
   with trusted publishing and creates a draft GitHub release with the notes from `CHANGELOG.md`.
5. Check the draft and publish it.

One-time setup, done by the maintainer: a trusted publishing policy on nuget.org for this repository,
workflow `release.yml` and environment `nuget`; a `nuget` environment in the repository settings with a
required reviewer; and a `NUGET_USER` secret holding the nuget.org user name. No API key is stored.
