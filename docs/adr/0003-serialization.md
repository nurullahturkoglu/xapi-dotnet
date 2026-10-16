# ADR-0003: Serialization

- Status: Accepted
- Date: 2026-10-16

## Context

System.Text.Json source generation does not work in Unity. Reflection-based System.Text.Json runs into code
stripping under IL2CPP. Validation needs strict reading: unknown properties, `null` values, property name
casing and duplicate keys all have to be detected.

## Options

1. Reflection-based System.Text.Json.
2. Source-generated System.Text.Json.
3. Newtonsoft.Json.
4. Hand-written readers and writers on `Utf8JsonReader` and `Utf8JsonWriter`.

## Decision

Option 4: one hand-written serializer, no adapters for other JSON libraries.

## Consequences

- The `net10.0` builds can be trimmed and published with NativeAOT; nothing relies on reflection.
- Errors carry the JSON path of the offending value.
- There is more code to write, and all of it is tested (conformance suite cases, specification examples,
  round-trip snapshots).
- Extensions are carried as `JsonElement`.
