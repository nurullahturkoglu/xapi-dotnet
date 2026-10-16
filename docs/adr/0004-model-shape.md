# ADR-0004: Model shape

- Status: Accepted
- Date: 2026-10-16

## Context

The statement model should be immutable, support deriving new values with `with`, have structural equality,
and compile with Unity's C# 9.

## Options

1. Mutable classes (TinCan.NET).
2. Classes with builders (xapi-java).
3. `sealed record` types.

## Decision

Option 3. Collections use our own `XapiList<T>` (immutable `IReadOnlyList<T>` with structural equality),
`LanguageMap` and `Extensions`. IRIs are an `Iri` record struct, times are `DateTimeOffset`, durations are
`XapiDuration`.

Polymorphism uses abstract records: `StatementObject` has the subtypes `Activity`, `Actor` (`Agent`, `Group`),
`StatementRef` and `SubStatement`. The inverse functional identifier `InverseFunctionalIdentifier` has the
subtypes `Mbox`, `MboxSha1Sum`, `OpenId` and `Account`.

## Consequences

- Record equality is structural equality. It is not the immutability comparison the standard defines for
  deciding whether a resent statement conflicts (HTTP 409); that comparison is a separate
  `StatementEquivalence` API.
