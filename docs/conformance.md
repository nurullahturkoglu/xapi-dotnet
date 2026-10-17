# Conformance

The LRS in this repository is tested with the
[ADL LRS Conformance Test Suite](https://github.com/adlnet/lrs-conformance-test-suite) on every pull request
and every push to `main`. This page explains how the suite runs, where the results stand, and where the suite
and the standard disagree.

Passing tests of the suite is not a certification, and the project does not claim to be certified or
endorsed by ADL. Results are stated as numbers: which suite commit, which of our commits, how many tests pass
in each battery and database.

## How the suite runs

The suite is pinned to commit
[`5bc232d`](https://github.com/adlnet/lrs-conformance-test-suite/commit/5bc232d349c60faded8240da698f195106091638)
(4 September 2025; there have been no upstream commits since). Upstream has no lockfile, so the repository
keeps its own (`eng/conformance/package-lock.json`) and installs the suite with `npm ci` on Node 22. The
pinned inputs are in `eng/conformance/cts.env`.

`.github/workflows/conformance.yml` runs both batteries, xAPI 1.0.3 (1,365 tests) and 2.0.0 (1,435 tests),
against `samples/LrsHost` on SQLite and on PostgreSQL: four jobs. Two more jobs run both batteries against
[lrsql](https://github.com/yetanalytics/lrsql) v0.9.9, a reference LRS that passes every test, to check the
pipeline itself.

The suite's exit code cannot be used as a gate: it is the number of failed tests, wraps at 256, and does not
count tests that never ran. `eng/conformance/CtsGate.cs` reads the suite's JSON log instead and fails the job
when:

- the log is for another battery, or the run did not finish;
- fewer tests exist than the expected total;
- more tests never ran than `maxNeverRan` allows (a failing setup step skips a whole suite of tests);
- fewer tests passed than `minPassed`;
- in strict mode, a test failed that is not on the allowlist.

The thresholds are in [`eng/conformance/floor.json`](../eng/conformance/floor.json), per battery and
database. They are ratchets: `minPassed` is only ever raised and `maxNeverRan` only ever lowered, in the pull
request that makes more tests pass.

The allowlists (`eng/conformance/known-failures-<version>.txt`) are empty. An entry may only be added for a
defect in the suite itself, with a link to the upstream issue.

[eng/conformance/README.md](../eng/conformance/README.md) describes the files and how to run a battery
locally.

## Current results

The LRS passes all tests of the ADL LRS Conformance Test Suite at commit `5bc232d`, in both batteries and on
both databases, as of commit `69bb8ff` on `main` (17 October 2026):

| Battery | Tests | Passed, SQLite | Passed, PostgreSQL | Did not run |
| --- | --- | --- | --- | --- |
| xAPI 1.0.3 | 1,365 | 1,365 | 1,365 | 0 |
| xAPI 2.0.0 | 1,435 | 1,435 | 1,435 | 0 |

Every `xapi-dotnet` row of `floor.json` now has `minPassed` equal to the total and `maxNeverRan` at 0, so a
single failing or skipped test fails the pull request that causes it.

## Where the suite and the standard disagree

In two places the suite expects behaviour that IEEE 9274.1.1 does not require. In both, the LRS and the
validator in `Xapi.Core` follow the suite. The suite is the only shared, executable measure of an LRS, and
ignoring it would leave tests that fail on purpose, which the gate is built to prevent. The cost to users is
small in both cases, and both are written down here and in the code.

### `context.revision` and `context.platform` (suite issue #278)

xAPI 1.0.3 (Data 2.4.6) allows `context.revision` and `context.platform` only when the statement's object is
an Activity. IEEE 9274.1.1 dropped that rule, but the 2.0.0 battery still tests it (requirements XAPI-00084
and XAPI-00085) and expects 400. This is reported as
[issue #278](https://github.com/adlnet/lrs-conformance-test-suite/issues/278).

The rule is applied to both versions: such a statement fails validation with `context.revision.object` or
`context.platform.object`, in the client before sending and in the LRS. A statement about an Agent or a
StatementRef has to leave those two properties out.

### Statement `version` values such as `1.1.0`

The 2.0.0 battery expects 400 for a statement whose `version` property is `should fail`, `0.9.9` or `1.1.0`.
IEEE 9274.1.1 says an LRS should not reject an otherwise valid statement because of its version.

Under the 2.0.0 rules, `Xapi.Core` accepts `1.0.x` and `2.0.0` as a statement's `version` and reports
anything else as `format.version`, so the LRS answers 400. In practice statements carry `1.0.x` or `2.0.0`,
which are accepted. The request header is a separate matter: there the standard itself requires 400 for
`2.1.0` and later, and the LRS answers 400 for every header that is not `1.0.x` or `2.0.x`.

## Other suite issues

The suite has a test that fails now and then when a timestamp's milliseconds end in zero, because some
serializers drop trailing zeros
([issue #281](https://github.com/adlnet/lrs-conformance-test-suite/issues/281)). `Xapi.Core` always writes a
fixed number of fraction digits, so the test is stable here.

The 1,922 statements that the suite generates from its templates also run as unit tests of the parser, with
the suite's expectations, including the two above (`tests/Xapi.Core.Tests/Conformance`).
