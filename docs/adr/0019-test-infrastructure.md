# ADR-0019: Test infrastructure

- Status: Accepted
- Date: 2026-10-16

## Decision

- xUnit v3 (`xunit.v3.mtp-v2`) on Microsoft.Testing.Platform.
- Our own snapshot helper instead of Verify (ADR-0017).
- Testcontainers for PostgreSQL and SQL LRS.
- BenchmarkDotNet for benchmarks.
- No mocking library; fake `HttpMessageHandler` implementations are written by hand.

TUnit was considered and not chosen, because xUnit is more widely used and most Testcontainers examples use it.

## Consequences

- Contributors need no unfamiliar test framework.
- HTTP behaviour is tested against explicit fakes and against real LRS instances, not mocks.
