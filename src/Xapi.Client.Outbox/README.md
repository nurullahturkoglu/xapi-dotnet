# Xapi.Client.Outbox

A durable outbox for Experience API (xAPI 2.0 and 1.0.3) statements on top of `Xapi.Client`. `EnqueueAsync`
stores a statement on disk and returns; the outbox delivers it later, in order and in batches, with retries,
error classification, dead letters and idempotent resend. Statements survive network outages, LRS downtime and
app restarts.

```bash
dotnet add package Xapi.Client.Outbox
```

## Usage

With `using Xapi;`, `using Xapi.Client;` and `using Xapi.Client.Outbox;`:

<!-- snippet: outbox-quick-start -->
```csharp
var http = new HttpClient();
var client = new XapiClient(http, new XapiClientOptions
{
    Endpoint = new Uri("https://lrs.example.com/xapi/"),
    Credentials = XapiCredentials.Basic(key, secret),
});
var store = await FileOutboxStore.OpenAsync(Path.Combine(dataDirectory, "xapi-outbox"));
var outbox = new StatementOutbox(client, store);

// Deliver in the background until the token is cancelled or the outbox is disposed
_ = outbox.RunAsync(shutdown.Token);

await outbox.EnqueueAsync(statement);

// Or deliver now, for example before the app is suspended
var result = await outbox.FlushAsync();
```

## Target frameworks

`netstandard2.0` (.NET Framework 4.6.2 and later) and `net10.0`. The `net10.0` build can be trimmed and
published with Native AOT, and adds a hosted service that delivers in the background.

## Links

- [Outbox guide](https://nurullahturkoglu.github.io/xapi-dotnet/outbox.html): delivery guarantees, dead letters, storage
- [API reference](https://nurullahturkoglu.github.io/xapi-dotnet/api/Xapi.Client.Outbox.html)
- [Source, issues and changelog](https://github.com/nurullahturkoglu/xapi-dotnet)

## License

Apache License 2.0. The package contains the `LICENSE`, `NOTICE` and `THIRD-PARTY-NOTICES.md` files.
