# Contributing

Thanks for your interest. This file covers how to build and test the code, how changes are proposed, and
how releases are made.

Please open an issue before starting on anything larger than a small fix, so we can agree on the approach
first. Questions are welcome in Discussions. Security problems go through [SECURITY.md](SECURITY.md), not
public issues. Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Prerequisites

- .NET 10 SDK 10.0.4xx (10.0.403 or a later patch). `global.json` pins the feature band so local builds run the same analyzers as CI.
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
working directory. For PostgreSQL, pass `--LrsHost:Database=postgres --LrsHost:ConnectionString=<connection
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

## Package validation

`EnablePackageValidation` (`eng/Library.targets`) runs during `dotnet pack`. Until 1.0 it only checks that
the `net10.0` and `netstandard2.0` builds of a package expose the same API. Once 1.0.0 is on nuget.org,
`PackageValidationBaselineVersion` is set to `1.0.0` and every pack is also compared with that release, so a
breaking change fails the build.

The baseline check does not know `[Experimental]`. A change to the storage API marked `XAPI0001` is allowed
in a minor release, but it fails the comparison like any other break (`CP0002` for a removed member, `CP0006`
for a member added to an interface, and so on). Record such a change deliberately:

1. Run `dotnet pack xapi-dotnet.slnx -c Release -p:ApiCompatGenerateSuppressionFile=true`. It writes the
   failures to `CompatibilitySuppressions.xml` in each affected project.
2. Check that every new suppression targets an experimental type or member, and nothing else.
3. Commit the file with the change and list the change under "Breaking" in `CHANGELOG.md`, saying it is
   experimental.

A suppression for anything that is not experimental is a breaking change and waits for the next major
version.

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

## Unity tests

`tests/Unity` is a Unity 6.3 project whose play mode tests use `Xapi.Core`, `Xapi.Client` and
`Xapi.Client.Outbox` as a Unity app would, installed by NuGetForUnity from `Assets/packages.config`, and the
Unity package in `src/Xapi.Unity` from the checkout. The client and outbox tests run once with
`HttpClientHandler` and once with `UnityWebRequestHandler`. The `Unity` workflow packs the three NuGet
packages, points the project at them with `tests/Unity/UseLocalPackages.cs`, and runs the tests in the editor, in a Linux
IL2CPP player and in a WebGL player in headless Chrome. It needs the `UNITY_LICENSE`, `UNITY_EMAIL` and
`UNITY_PASSWORD` secrets and is skipped without them. Android and iOS run locally only.

To run the tests locally, install Unity 6000.3.26f1 with Mac (or Windows or Linux) Build Support (IL2CPP),
start the LRS image, restore the packages and run the tests in the editor or in a player:

```bash
docker run -d -p 8080:8080 -e LrsHost__Key=unity_key -e LrsHost__Secret=unity_secret -e "LrsHost__AllowedOrigins=*" \
  ghcr.io/nurullahturkoglu/xapi-lrs:0.3.0
cd tests/Unity
dotnet tool restore
dotnet nugetforunity restore .
Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults playmode.xml
Unity -batchmode -projectPath . -runTests -testPlatform StandaloneOSX -testResults player.xml
```

Without the restore step, batch mode stops on compiler errors before NuGetForUnity can restore anything. To
test packages packed from your checkout instead of the versions on nuget.org, run
`dotnet run tests/Unity/UseLocalPackages.cs -- artifacts/packages` before the restore, and undo its changes to
`Assets/packages.config` and `Assets/NuGet.config` afterwards. When a dependency floor changes (see below),
`UseLocalPackages.cs` fails until `Assets/packages.config` lists the new version: install the packages again
from the NuGet window, or run `Unity -batchmode -quit -projectPath . -executeMethod XapiPackages.Install` with
`XAPI_PACKAGE_VERSION` set to a published version.

The WebGL tests need a browser. Unity opens the default one when it runs player tests, so build the player
only and run it in headless Chromium instead.
`XAPI_UNITY_PLAYER_PATH`, or `-xapiPlayerPath <path>` on the command line as CI passes it, makes a player test
run build the player into that path and quit (`Assets/Tests/BuildOnly.cs`); `ConsoleReporter.cs` writes the
results to the browser console, and `tests/Unity/WebGL/run.mjs` serves the player, runs it and prints them. The
LRS needs `LrsHost__AllowedOrigins`, as in the command above. The test runner changes some player settings while
it builds and restores them at the end, which `BuildOnly.cs` skips, so undo the changes to
`ProjectSettings/ProjectSettings.asset` afterwards.

```bash
XAPI_UNITY_PLAYER_PATH=$PWD/Builds/WebGL Unity -batchmode -projectPath . -runTests -testPlatform WebGL
cd WebGL
npm ci
npx playwright-core install chromium --only-shell
node run.mjs ../Builds/WebGL
```

The build takes about 10 minutes. Tests marked `HttpClientHandler` are skipped in WebGL, because that handler
cannot open connections in a browser.

Android players report the same way, to logcat. Build one with Android Build Support installed, then run it on a
device or emulator with Android 7.1 (API level 25) or later. The player is built for arm64, so an emulator needs an
arm64 system image, which runs on Apple silicon. `adb reverse` makes the LRS on your machine the player's
`localhost:8080`, and the test runner names the app `com.UnityTestRunner.UnityTestRunner`:

```bash
XAPI_UNITY_PLAYER_PATH=$PWD/Builds/Android/xapi-tests.apk Unity -batchmode -projectPath . -runTests -testPlatform Android
adb reverse tcp:8080 tcp:8080
adb install -r Builds/Android/xapi-tests.apk
adb logcat -c
adb shell monkey -p com.UnityTestRunner.UnityTestRunner -c android.intent.category.LAUNCHER 1
adb logcat -s Unity | grep XAPI-TEST
```

For iOS, Unity writes an Xcode project and Xcode builds the player. With iOS Build Support and Xcode installed, set
Target SDK to Simulator SDK and the simulator architecture to arm64 (on Apple silicon) in the iOS player settings,
then build without signing and run the app in a booted simulator, which reaches the LRS at `localhost:8080`
directly. Undo the settings changes afterwards.

```bash
XAPI_UNITY_PLAYER_PATH=$PWD/Builds/iOS Unity -batchmode -projectPath . -runTests -testPlatform iOS
xcodebuild -project Builds/iOS/Unity-iPhone.xcodeproj -scheme Unity-iPhone -sdk iphonesimulator \
  -destination 'generic/platform=iOS Simulator' -derivedDataPath Builds/iOS/DerivedData CODE_SIGNING_ALLOWED=NO build
xcrun simctl boot "iPhone 17 Pro"
xcrun simctl install booted Builds/iOS/DerivedData/Build/Products/Debug-iphonesimulator/Xapi.Unity.Tests.app
xcrun simctl launch --console-pty booted com.xapi-dotnet.Xapi.Unity.Tests | grep XAPI-TEST
```

## Dependency floors

On `netstandard2.0`, `Xapi.Core`, `Xapi.Client` and `Xapi.Client.Outbox` depend on System.Text.Json,
Microsoft.Bcl.AsyncInterfaces, Microsoft.Bcl.TimeProvider and System.Diagnostics.DiagnosticSource. The versions
in `Directory.Packages.props` become minimum versions in the nuspec, and a .NET Framework or Unity app that pins
an older one would have to upgrade. So each is a floor: the lowest version that has every API the libraries use
and no known vulnerability, not the newest. On `net10.0` these come with the shared framework.

To check a candidate, set it in `Directory.Packages.props` and run `dotnet build`: the `netstandard2.0` builds
fail on an API the version lacks, and NuGet audit fails restore on a version with a known vulnerability. Raise a
floor only when the code needs a newer API or an advisory covers the floor, and then also change the comment
next to it, `DependencyFloors` in `tests/Xapi.PackageTests/PackageUnderTest.cs` and `tests/Unity/Assets/packages.config`. Dependabot ignores these
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

Releases are made by the maintainer. The NuGet package version comes from the git tag through MinVer. The
Unity package in `src/Xapi.Unity` is installed from git, so its `package.json` holds the version too.

1. In a pull request, rename `[Unreleased]` in `CHANGELOG.md` to `[x.y.z] - YYYY-MM-DD`, add a new empty
   `[Unreleased]` section and the comparison links, and record measured results (test counts, conformance
   results, benchmarks where relevant). The docs pin the image version, so also move the `xapi-lrs:<version>`
   tags in the docs to the new version (`git grep 'xapi-lrs:'`). Set `version`
   in `src/Xapi.Unity/package.json` to the new version, and move the `#v<version>` in the Unity install URLs
   (`git grep 'Xapi.Unity#v'`). Merge it.
2. Tag the merge commit and push the tag: `git tag vX.Y.Z && git push origin vX.Y.Z`. Previews use
   `vX.Y.Z-preview.N`.
3. The `Release` workflow builds, tests and packs, checks that the package versions match the tag and that
   `CHANGELOG.md` has a section for it, and attests build provenance for the nupkgs. It then waits for
   approval of the `nuget` environment.
4. Review the run and approve the `nuget` environment in GitHub. The workflow then publishes to nuget.org
   with trusted publishing and creates a draft GitHub release with the notes from `CHANGELOG.md`. The same
   approval releases the `samples/LrsHost` image: the `image` job builds it from the tagged commit for
   linux/amd64 and linux/arm64, pushes it to `ghcr.io/nurullahturkoglu/xapi-lrs` with `GITHUB_TOKEN`, tagged
   with the version and, unless the version is a preview, `latest`, and attests its build provenance. If only
   that job fails, re-run it on its own; the packages are already published.
5. Check the draft and publish it.

One-time setup, done by the maintainer: a trusted publishing policy on nuget.org for this repository,
workflow `release.yml` and environment `nuget`; a `nuget` environment in the repository settings with a
required reviewer; and a `NUGET_USER` secret in that environment holding the nuget.org user name. No API key is stored.

The first release creates the `xapi-lrs` package on ghcr.io as a private package. Once, after that release,
open the package settings on GitHub (Packages, `xapi-lrs`, Package settings): change its visibility to
public, and if the package page does not show this repository, connect it under "Connect repository" (the
image's `org.opencontainers.image.source` label normally links it on its own).
