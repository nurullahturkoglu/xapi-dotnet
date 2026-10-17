# FakeLrsInTests

Tests for code that sends xAPI statements, run against a real LRS that lives inside the test process. No
Docker, no port and no network: `Xapi.Lrs` is hosted with ASP.NET Core's `TestServer`, and `Xapi.Client`
talks to it through the `HttpClient` that `TestServer` creates.

```sh
dotnet test --project samples/FakeLrsInTests
```

## Files

| File | What it is |
| --- | --- |
| `CourseProgress.cs` | The code under test: records a completion and saves and reads a bookmark through `XapiClient`. |
| `FakeLrs.cs` | An xUnit fixture that starts the LRS and hands out clients. |
| `CourseProgressTests.cs` | Tests that run the code and then check what the LRS stored, for xAPI 1.0.3 and 2.0.0. |

## How the fake LRS works

`FakeLrs` builds a `WebApplication` with `UseTestServer()`, adds the LRS with one Basic credential that has
every scope, and maps it under `/xapi`. `CreateClient` returns an `XapiClient` whose `HttpClient` comes from
`TestServer.CreateClient()`, so requests go through the full ASP.NET Core pipeline without a socket.

The LRS stores its data in a SQLite file in a new temporary folder, which is deleted when the fixture is
disposed. Connection pooling is off so that no connection keeps the file open, which would stop the folder
from being deleted on Windows. An in-memory SQLite database would need a connection held open for the
lifetime of the fixture.

As an `IClassFixture`, one LRS is shared by the tests of a class. Tests that should not see each other's data
can use different Agents or Activity ids, as these do, or the fixture can be made per test.

## Use it in your own tests

Copy `FakeLrs.cs` into your test project and reference `Xapi.Client`, `Xapi.Lrs.Sqlite` and
`Microsoft.AspNetCore.TestHost`. Pass `fakeLrs.CreateClient()` wherever your code takes an `XapiClient`, or
`fakeLrs.CreateClient(XapiVersion.V1_0_3)` to test against xAPI 1.0.3. If your code creates its own
`HttpClient`, let it accept one, or register the client in your test's service collection with the
`HttpClient` from the test server.
