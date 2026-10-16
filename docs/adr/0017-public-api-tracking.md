# ADR-0017: Public API tracking

- Status: Accepted
- Date: 2026-10-16

## Context

Changes to the public API should be visible in review. Common approaches are PublicApiAnalyzers with
`PublicAPI.*.txt` files (Polly, Npgsql) and PublicApiGenerator with a snapshot test (Serilog, Mapperly,
TUnit). Verify, the usual snapshot library, fails builds since 33.x unless an "Open Source Maintenance Fee"
license declaration is made.

## Decision

- PublicApiGenerator renders each shipped assembly's public API, and a small snapshot helper in
  `tests/Xapi.PublicApi.Tests` compares it with `ApiSnapshots/<Assembly>.approved.txt`. On a mismatch it
  writes `<Assembly>.received.txt` and the test fails.
- Package validation (`EnablePackageValidation`) is on; a baseline version is set from 1.0.0.
- Verify is not used.

## Consequences

- API changes appear as a diff of the approved file in the pull request.
- No extra analyzer package and no license declaration on the maintainer's behalf.
