# ADR-0001: Product shape

- Status: Accepted
- Date: 2026-10-16

## Context

SQL LRS (yetanalytics/lrsql) is free and passes both conformance batteries, so a standalone LRS product would
add little. The concrete pain in .NET is on the sending side: teams wrap the frozen TinCan.NET client and
write their own outbox and retry code (CMU SEI copied about 880 lines of it across six services in 2026).
An embeddable LRS is a niche need, but it proves conformance and gives .NET developers a real LRS for tests
and offline setups.

## Options

1. Client SDK only.
2. Client SDK plus a standalone LRS product.
3. Client SDK and outbox, plus an LRS library that is embedded in an ASP.NET Core application.

## Decision

Option 3. Work starts with Core, Client and Outbox; the LRS library follows.

## Consequences

- The Docker image of the LRS is a sample (`samples/LrsHost`), not a product.
- There is no administration UI.
- The LRS is measured by the ADL conformance suite (ADR-0015), not by a feature list.
