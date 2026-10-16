# ADR-0015: Conformance gate

- Status: Accepted
- Date: 2026-10-16

## Context

The ADL LRS Conformance Test Suite is the only shared yardstick for an LRS. It is barely maintained (last
maintainer comment 4 September 2025), has no lockfile upstream, and its exit code cannot be used as a gate:
it equals the failure count, wraps at 256, and does not count tests that never ran.

## Decision

- The suite is pinned to a commit (`5bc232d`) and installed from our own lockfile, on Node 22.
- A gate program, `eng/conformance/CtsGate.cs`, reads the suite's JSON log. The job fails if the run did not
  finish, if the total is below the expected count, if any test did not run, or if a test outside the
  allowlist failed.
- `eng/conformance/floor.json` records the minimum number of passing tests per battery and database. It only
  goes up; a drop fails CI.
- The allowlist starts empty, and every entry links to an upstream suite issue.
- Until the LRS exists, the job runs against SQL LRS to test the pipeline itself. Then it targets
  `samples/LrsHost`.
- An optional nightly job runs against the suite's `master` and does not block.

## Consequences

- A regression in conformance is visible in the pull request that causes it.
- Results can be stated precisely: suite commit, our commit, passing count per battery and database. The
  project says it "passes the ADL LRS conformance test suite (commit X)" and never claims certification.
