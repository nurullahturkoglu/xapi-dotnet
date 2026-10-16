# ADR-0006: Validation and error model

- Status: Accepted
- Date: 2026-10-16

## Context

The LRS must return meaningful errors in its 400 responses. The client should be able to check statements
before sending them. Exceptions are expensive in Unity and awkward for reporting many problems at once.

## Decision

- Parsing returns `XapiParseResult<T>`, and errors are `XapiError(Code, Path, Message)`.
- All errors are collected; validation does not stop at the first one.
- Exceptions are thrown only at API edges: `GetValueOrThrow` and the client's `ValidateBeforeSend`.
- Error codes are stable strings such as `statement.actor.ifi.multiple`, and the documentation maps each
  code to the clause of the standard it enforces.
- Reading has two modes: `Strict` for LRS input, and `Lenient` for the client reading LRS responses, where
  unknown properties are ignored.

## Consequences

- Error codes are public contract and follow the same compatibility rules as the API.
- One invalid statement yields a complete list of problems with JSON paths.
