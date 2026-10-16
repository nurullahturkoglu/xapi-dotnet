# ADR-0007: Time and duration

- Status: Accepted
- Date: 2026-10-16

## Context

System.Text.Json drops trailing zeros from fractional seconds. Timestamps whose milliseconds end in zero
then trigger a flaky test in the conformance suite (adlnet/lrs-conformance-test-suite#281). The standard
compares durations to 0.01 seconds and allows an LRS to truncate them.

## Decision

- Reading accepts RFC 3339 timestamps (in 2.0 also with a space instead of `T`) and converts them to UTC.
- Writing always uses `Z` and at least three fractional digits; seven when there are sub-millisecond ticks.
- Durations keep their original string. Comparison is to 0.01 seconds.

## Consequences

- Written timestamps have a fixed shape, which avoids the suite issue and keeps snapshots stable.
- A duration read and written again is unchanged.
