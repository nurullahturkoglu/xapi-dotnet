using System.Text;
using Xapi.Client;

namespace Xapi.Documentation.Tests.Snippets;

// docs/migrating-from-tincan.md, the xapi-dotnet side of each example. Compiled, not run.
internal static class MigrationSnippets
{
    public static async Task Send(HttpClient http)
    {
        // snippet: tincan-send
        var client = new XapiClient(http, new XapiClientOptions
        {
            Endpoint = new Uri("https://lrs.example.com/xapi/"),
            Credentials = XapiCredentials.Basic("key", "secret"),
            Version = XapiVersion.V1_0_3, // what TinCan.NET sends; leave it unset to negotiate
        });

        var statement = new Statement(
            Agent.FromMbox("learner@example.com", "Ada Learner"),
            Verbs.Completed,
            new Activity(new Iri("https://example.com/courses/fire-safety")));

        try
        {
            var id = await client.SaveStatementAsync(statement);
            Console.WriteLine(id);
        }
        catch (XapiHttpException exception)
        {
            Console.WriteLine(exception.Message);
        }
        // end snippet
    }

    public static async Task Query(XapiClient client, Agent learner)
    {
        // snippet: tincan-query
        var query = new StatementQuery
        {
            Agent = learner,
            Verb = Verbs.Completed.Id,
            Since = DateTimeOffset.UtcNow.AddDays(-7),
        };

        await foreach (var statement in client.QueryAllStatementsAsync(query))
        {
            Console.WriteLine(statement.Id);
        }
        // end snippet
    }

    public static async Task State(XapiClient client, Agent learner)
    {
        // snippet: tincan-state
        var key = new StateKey(new Iri("https://example.com/courses/fire-safety"), learner, "bookmark");

        await client.PutDocumentAsync(key, new XapiDocument(Encoding.UTF8.GetBytes("page-3"), "text/plain"), DocumentPrecondition.IfNoneMatchAny);

        var document = await client.GetDocumentAsync(key);
        var bookmark = document is null ? null : Encoding.UTF8.GetString(document.Content.Span);
        // end snippet

        _ = bookmark;
    }
}
