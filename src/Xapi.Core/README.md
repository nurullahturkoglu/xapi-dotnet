# Xapi.Core

Experience API (xAPI 2.0, IEEE 9274.1.1, and 1.0.3) models for .NET: the statement model, strict JSON reading
and writing for both versions, validation, and statement comparison. JSON is read and written without
reflection. `Xapi.Client` and `Xapi.Lrs` build on this package; use it on its own to read, check or write xAPI
JSON.

```bash
dotnet add package Xapi.Core
```

## Usage

With `using Xapi;`, `using Xapi.Json;` and `using Xapi.Validation;`:

<!-- snippet: core-json -->
```csharp
var parsed = XapiJson.ParseStatement(json, XapiVersion.V2_0_0);
if (!parsed.IsSuccess)
{
    foreach (var error in parsed.Errors)
    {
        Console.WriteLine(error); // "<JSON path>: <message> (<error code>)"
    }

    return;
}

var statement = parsed.Value with { Timestamp = DateTimeOffset.UtcNow };
var errors = StatementValidator.Validate(statement, XapiVersion.V1_0_3);
Console.WriteLine(errors.Count == 0
    ? XapiJson.SerializeStatement(statement, XapiVersion.V1_0_3)
    : "Not valid as xAPI 1.0.3: " + string.Join("; ", errors));
```

## Target frameworks

`netstandard2.0` (.NET Framework 4.6.2 and later) and `net10.0`. The `net10.0` build can be trimmed and
published with Native AOT.

## Links

- [Documentation](https://nurullahturkoglu.github.io/xapi-dotnet/getting-started.html)
- [xAPI versions](https://nurullahturkoglu.github.io/xapi-dotnet/versions.html): what differs between 1.0.3 and 2.0.0
- [API reference](https://nurullahturkoglu.github.io/xapi-dotnet/api/Xapi.html)
- [Source, issues and changelog](https://github.com/nurullahturkoglu/xapi-dotnet)

## License

Apache License 2.0. The package contains the `LICENSE`, `NOTICE` and `THIRD-PARTY-NOTICES.md` files.
