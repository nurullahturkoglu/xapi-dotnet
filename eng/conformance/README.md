# Conformance testing

This directory runs the [ADL LRS Conformance Test Suite](https://github.com/adlnet/lrs-conformance-test-suite) (CTS) against an LRS and decides from the suite's JSON log whether the run passed. `.github/workflows/conformance.yml` runs it on every push to `main` and every pull request, and `run-local.sh` runs the same steps on your machine.

Until our own LRS exists (milestone M4), the target is [lrsql](https://github.com/yetanalytics/lrsql), a conformant reference LRS. It passes all 1435 tests of the xAPI 2.0.0 battery and all 1365 of the 1.0.3 battery, so these runs check the pipeline itself. From M4 on the same job also runs against `samples/LrsHost`.

| File | Purpose |
|---|---|
| `cts.env` | Pinned inputs: CTS repository and commit, Node major, lrsql and PostgreSQL images (tag plus digest) |
| `package.json`, `package-lock.json` | The suite's `package.json` at the pinned commit and a lockfile we generated for it, since upstream has none. Both are copied into the suite checkout before `npm ci` |
| `CtsGate.cs` | .NET file-based app that reads the run log, applies the gate and writes JUnit XML and a Markdown summary |
| `floor.json` | Expected test count, minimum passed count and strictness per target, database and battery |
| `known-failures-<version>.txt` | Allowlist of known suite defects, empty by default |
| `patches/` | Optional `git apply` patches for the suite, applied in name order |
| `run-local.sh` | Local runner |

## Running locally

You need Docker, Node 22, git, curl and the .NET SDK from `global.json`.

```bash
eng/conformance/run-local.sh --target lrsql --xapi 2.0.0                  # reference run against lrsql, SQLite
eng/conformance/run-local.sh --target lrsql --xapi 1.0.3 --db postgres    # lrsql on PostgreSQL
LRS_URL=http://localhost:5000/xapi eng/conformance/run-local.sh --target local --xapi 2.0.0
```

The first run fetches the suite at the pinned commit into `.cts/<sha>/` and installs its dependencies with `npm ci`. Later runs reuse that checkout and reinstall only when the lockfile changes. Each run writes the suite log (`cts-log.json`), `junit.xml`, `summary.md`, the console output and the LRS container log to `.cts/results/<target>-<db>-<xapi>/`.

With `--target lrsql` the script starts the pinned lrsql image on port 8080 (`CTS_PORT` changes it) and removes the containers when it exits, unless you pass `--keep`. With `--target local` it expects an LRS already listening at `LRS_URL` with Basic credentials `LRS_KEY`/`LRS_SECRET` (defaults `cts_key`/`cts_secret`), and gates the run with the `xapi-dotnet` entries of `floor.json`.

A battery takes about 45 seconds against lrsql. The lrsql image is linux/amd64 only, so on Apple silicon it runs under emulation and starts in about 15 seconds.

## How the gate works

The suite's exit code is the number of failed tests. It wraps at 256 and ignores tests that never ran, so it is printed but not used. The gate reads the JSON log instead:

```bash
dotnet run eng/conformance/CtsGate.cs -- <log.json> --floor-file eng/conformance/floor.json \
  --target lrsql --db sqlite --xapi 2.0.0 --allowlist eng/conformance/known-failures-2.0.0.txt \
  --junit junit.xml --summary summary.md
```

It fails (exit code 1) when any of these holds:

- the log reports a battery other than `--xapi`, or a `state` other than `finished`;
- the total test count is below `total` from `floor.json` (or `--floor`);
- more tests never ran (`total - passed - failed`) than `maxNeverRan` from `floor.json` (default 0). A failing root hook such as a wrong password or a rejected version header looks like 1 failure and 1434 tests that never ran. A failing `before` hook inside one suite skips only that suite's tests, which is expected while the resource it needs is not implemented yet;
- fewer than `minPassed` tests passed (or `--min-passed`);
- in strict mode, any failed test whose title path is not in the allowlist.

Tests are identified by their title path, `Group > Suite > test title`, exactly as the gate prints it after `FAIL`. Requirement IDs (`XAPI-NNNNN`) cannot be used because the 2.0-only tests have none. A path that occurs more than once (some suite configs repeat a name) is covered by one allowlist entry.

The JUnit file has one test case per suite test, with allowlisted failures as `<skipped>`. The Markdown summary goes to the job summary in CI. The gate also warns about allowlist entries that no longer fail, and about `minPassed` values below the current passed count.

## Allowlist policy

Both allowlists start empty. An entry is allowed only for a defect in the suite itself, never for a bug in our LRS, and it must link to the upstream issue or pull request:

```text
Group > Suite > test title # https://github.com/adlnet/lrs-conformance-test-suite/issues/281
```

Remove an entry in the same pull request that makes the test pass. lrsql passes everything at the pinned commit, so the `lrsql` rows keep `minPassed` equal to `total` and allowlist entries cannot hide a regression there.

## Raising the floor

`floor.json` is keyed by target, database and battery. `total` is the number of tests at the pinned suite commit and changes only when the commit changes. `minPassed` is a ratchet: it is only ever raised, and CI fails when the passed count drops below it.

The `xapi-dotnet` rows have `strict: false`, so failures are reported but only a drop below `minPassed`, a short run or more than `maxNeverRan` tests that never ran fail the job. `maxNeverRan` is a ratchet in the other direction: it is only ever lowered, and the gate prints a note when it can be. It is 0 for every row once the LRS implements all resources. When a pull request makes more tests pass, set `minPassed` to the new passed count in the same pull request; the gate prints a note when it can be raised. Once a battery passes completely, set `minPassed` to `total` and `strict` to `true`.

## Updating the pinned suite commit

The suite has had no upstream commits since 4 Sep 2025, so updates should be rare.

1. Set `CTS_SHA` in `cts.env`.
2. Regenerate the lockfile with Node 22:

   ```bash
   source eng/conformance/cts.env
   tmp=$(mktemp -d)
   git init -q "$tmp"
   git -C "$tmp" fetch -q --depth 1 "$CTS_REPO" "$CTS_SHA"
   git -C "$tmp" checkout -q FETCH_HEAD
   (cd "$tmp" && npm install --package-lock-only --no-audit --no-fund)
   cp "$tmp/package.json" "$tmp/package-lock.json" eng/conformance/
   ```

3. Check that every patch in `patches/` still applies, and drop the ones that upstream has merged.
4. Run both batteries against lrsql with `run-local.sh`. If the test counts changed, update `total` in `floor.json` and the `lrsql` `minPassed` values to match.
5. Check that the allowlist paths still exist; the gate warns about stale entries.

To move to a newer lrsql release, take the tag and digest from Docker Hub, update `LRSQL_IMAGE` in `cts.env` and run both batteries again.

## Patching the suite

If a suite defect blocks us and upstream does not respond, add a minimal patch as `patches/NNNN-short-name.patch` (made with `git diff` inside the suite checkout) and start it with a comment line that links the upstream issue. Prefer an allowlist entry when the defect affects only a few tests.
