# ADR-0002: Target frameworks

- Status: Accepted
- Date: 2026-10-16

## Context

Unity runs .NET Standard 2.1 API level with C# 9, and its move to CoreCLR has no date. .NET Framework users
will migrate from TinCan.NET, which targets `net35`. .NET 8 and .NET 9 reach end of support on
10 November 2026.

## Options

1. `netstandard2.0` and `net10.0`.
2. `netstandard2.1` and `net10.0`.
3. `net8.0` and `net10.0`.

## Decision

`Xapi.Core`, `Xapi.Client` and `Xapi.Client.Outbox` target `netstandard2.0;net10.0`. The `Xapi.Lrs`
packages target `net10.0` only.

## Consequences

- `netstandard2.0` covers Unity and .NET Framework 4.6.2 and later.
- PolySharp (compile time only) provides `init`, records and nullable attributes on `netstandard2.0`.
- The public API has no `required` members, because the C# 9 compiler in Unity cannot call constructors of
  types that have them.
- On `netstandard2.0`, System.Text.Json and Microsoft.Bcl.AsyncInterfaces are package dependencies.
