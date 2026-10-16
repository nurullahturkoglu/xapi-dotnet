# ADR-0014: Observability

- Status: Accepted
- Date: 2026-10-16

## Decision

- Tracing and metrics use `System.Diagnostics`: `ActivitySource("Xapi.Lrs")` and `Meter("Xapi.Lrs")`.
  There is no dependency on OpenTelemetry packages; users connect them with `AddSource` and `AddMeter`.
- LRS instruments: `xapi.lrs.statements.stored`, `xapi.lrs.request.duration`, `xapi.lrs.errors`.
- Client and outbox: `Meter("Xapi.Client")` with sent, retried and dead-lettered counts and queue depth.
- A health check: `AddXapiLrsHealthCheck()`.

## Consequences

- No telemetry cost or dependency for users who do not collect it.
- Instrument names are public contract once released.
