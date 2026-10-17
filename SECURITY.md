# Security Policy

## Supported versions

The project is in early development and nothing has been released yet. Once
packages are published, only the latest release line receives fixes.

| Version | Supported |
| ------- | --------- |
| 0.x (latest) | Yes |
| Older 0.x | No |

## Reporting a vulnerability

Please do not open a public issue for security problems. Use the private
"Report a vulnerability" form under the Security tab of this repository, or
email info@scriptyuvasi.com. You will get a reply within a week.

Include the package and version, what an attacker can do, and a minimal
reproduction if you have one. Once a fix is released, the advisory is
published on GitHub with credit to the reporter unless you ask otherwise.

## Scope notes

The embedded LRS (`Xapi.Lrs`) authenticates requests with HTTP Basic
credentials or delegates to an ASP.NET Core authentication scheme you
configure. Transport security, rate limiting and network exposure are the
responsibility of the host application. [docs/lrs.md](docs/lrs.md) describes the
request limits, rate limiting, CORS and logging settings to review before exposing
an LRS.

Problems in the ADL LRS Conformance Test Suite or in third-party LRS products
should go to their own maintainers.
