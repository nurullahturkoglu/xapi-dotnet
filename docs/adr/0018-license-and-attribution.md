# ADR-0018: License and attribution

- Status: Accepted
- Date: 2026-10-16

## Context

The IEEE xAPI Base Standard documentation is Apache-2.0. The ADL conformance suite is MIT. xapi-schema test
data is EPL-1.0. XML documentation ships inside the NuGet packages, so quoting the standard there is
redistribution.

## Decision

- The project is licensed under Apache-2.0.
- `NOTICE` holds the project copyright and the independence notice.
- `THIRD-PARTY-NOTICES.md` is packed into every nupkg, because XML documentation quotes the IEEE text.
- Files derived from the conformance suite say so in their header: "Derived from
  adlnet/lrs-conformance-test-suite (MIT)".
- xapi-schema data (EPL-1.0) is not copied.
- The standard is quoted only from the open source Markdown repository, never from the paid PDF.

## Consequences

- Same license as the standard's text, SQL LRS, TinCan.NET and xapi-java, with an explicit patent grant.
- Contributors must attribute quoted or derived material as described in `THIRD-PARTY-NOTICES.md`.
