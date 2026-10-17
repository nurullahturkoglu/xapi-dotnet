using Xapi.Client;
using Xapi.Client.Outbox;

namespace Xapi.Documentation.Tests.Snippets;

// docs/outbox.md and src/Xapi.Client.Outbox/README.md. Compiled, not run.
internal static class OutboxSnippets
{
    public static async Task WithoutDependencyInjection(string key, string secret, string dataDirectory, Statement statement, CancellationTokenSource shutdown)
    {
        // snippet: outbox-quick-start
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
        // end snippet
        _ = result;
    }
}
