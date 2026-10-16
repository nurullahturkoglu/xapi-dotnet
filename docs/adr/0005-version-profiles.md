# ADR-0005: xAPI version profiles

- Status: Accepted
- Date: 2026-10-16

## Context

xAPI 1.0.3 and 2.0.0 differ little in the data model (`contextAgents`, `contextGroups`, the `version` value,
a space allowed instead of `T` in timestamps) and in a handful of behaviour rules. An LRS has to serve both
versions in the same process.

## Options

1. Two separate models.
2. One model and a global version setting (as xapi-schema does).
3. One model and the version passed with every call.

## Decision

Option 3. `XapiVersion` is a required parameter of reading, writing and validation. Properties that exist
only in 2.0 are validation errors under 1.0.3.

## Consequences

- One process can handle both versions, which the LRS needs.
- Callers always state which version they mean; there is no ambient default to get wrong.
