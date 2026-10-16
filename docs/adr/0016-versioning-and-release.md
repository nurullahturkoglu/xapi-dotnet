# ADR-0016: Versioning and release

- Status: Accepted
- Date: 2026-10-16

## Context

nuget.org API keys created before 17 August 2026 expire on 1 November 2026, and new keys last at most
30 days. Every release should be an explicit decision by the maintainer.

## Decision

- Versions come from git tags through MinVer, with the `v` prefix. No file holds the version.
- Pushing a `v*` tag starts `release.yml`: build, test, pack, package tests, build provenance attestation.
- Publishing waits for approval of the `nuget` environment, then uses nuget.org trusted publishing
  (`NuGet/login`) and creates a draft GitHub release with notes from `CHANGELOG.md`.

## Consequences

- No long-lived API key exists anywhere.
- Each release needs two human actions: pushing the tag and approving the environment.
- During 0.x, breaking changes are allowed and are listed under "Breaking" in the changelog.
