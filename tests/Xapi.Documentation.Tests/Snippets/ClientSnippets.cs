using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xapi.Client;

namespace Xapi.Documentation.Tests.Snippets;

// docs/client.md. Compiled, not run.
internal static class ClientSnippets
{
    public static async Task Version(HttpClient http, string key, string secret)
    {
        // snippet: client-version
        var client = new XapiClient(http, new XapiClientOptions
        {
            Endpoint = new Uri("https://lrs.example.com/xapi/"),
            Credentials = XapiCredentials.Basic(key, secret),
            Version = XapiVersion.V1_0_3,
        });
        // end snippet

        _ = await client.GetVersionAsync();
    }

    public static IXapiClient Bearer(HttpClient http, ITokenSource tokens)
    {
        // snippet: client-bearer
        var client = new XapiClient(http, new XapiClientOptions
        {
            Endpoint = new Uri("https://lrs.example.com/xapi/"),
            Credentials = XapiCredentials.Bearer(tokens.GetAccessTokenAsync),
        });
        // end snippet

        return client;
    }

    public static void Resilience(WebApplicationBuilder builder, string key, string secret)
    {
        // snippet: client-resilience
        builder.Services
            .AddXapiClient(options =>
            {
                options.Endpoint = new Uri("https://lrs.example.com/xapi/");
                options.Credentials = XapiCredentials.Basic(key, secret);
            })
            .HttpClient.AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
        // end snippet
    }

    public static async Task Statements(IXapiClient client, Statement statement, Agent learner)
    {
        // snippet: client-statements
        var ids = await client.SaveStatementsAsync([statement, statement with { Verb = Verbs.Passed }]);

        var stored = await client.GetStatementAsync(ids[0]);
        var voidingId = await client.VoidStatementAsync(ids[1], learner);
        var voided = await client.GetVoidedStatementAsync(ids[1]);
        // end snippet

        _ = (stored, voidingId, voided);
    }

    public static async Task Query(IXapiClient client)
    {
        // snippet: client-query
        var query = new StatementQuery
        {
            Agent = Agent.FromMbox("learner@example.com"),
            Verb = Verbs.Completed.Id,
            Since = DateTimeOffset.UtcNow.AddDays(-7),
        };

        await foreach (var statement in client.QueryAllStatementsAsync(query))
        {
            Console.WriteLine($"{statement.Id} {statement.Timestamp:u}");
        }
        // end snippet
    }

    public static async Task Documents(IXapiClient client, Iri course, Agent learner)
    {
        // snippet: client-documents
        var key = new StateKey(course, learner, "bookmark");

        var current = await client.GetDocumentAsync(key);
        var precondition = current?.ETag is { } etag
            ? DocumentPrecondition.IfMatch(etag)
            : DocumentPrecondition.IfNoneMatchAny;

        try
        {
            await client.PutDocumentAsync(key, new XapiDocument(Encoding.UTF8.GetBytes("module-3/page-1"), "text/plain"), precondition);
        }
        catch (XapiPreconditionFailedException)
        {
            // Another writer changed the document after it was read: read it again and decide
        }
        // end snippet
    }

    public static async Task Merge(IXapiClient client, Iri course, Agent learner)
    {
        // snippet: client-merge
        using var changes = JsonDocument.Parse("""{ "lastPage": 12, "theme": "dark" }""");
        var key = new StateKey(course, learner, "preferences");

        await client.MergeDocumentAsync(key, changes.RootElement);

        var preferences = await client.GetDocumentAsync(key);
        var theme = preferences?.ToJson().GetProperty("theme").GetString();
        // end snippet

        _ = theme;
    }

    public static async Task Attachments(IXapiClient client, Iri course, Agent learner)
    {
        // snippet: client-attachments
        var certificate = AttachmentPayload.Create(await File.ReadAllBytesAsync("certificate.pdf"), "application/pdf");
        var statement = new Statement(learner, Verbs.Completed, new Activity(course))
        {
            Attachments =
            [
                certificate.ToAttachment(
                    new Iri("https://example.com/attachment-usages/certificate"),
                    LanguageMap.Create("en-US", "Certificate")),
            ],
        };

        await client.SaveStatementAsync(statement, [certificate]);

        var page = await client.QueryStatementsAsync(new StatementQuery { Activity = course, Attachments = true });
        foreach (var payload in page.Attachments)
        {
            Console.WriteLine($"{payload.Sha2} {payload.ContentType} {payload.Content.Length} bytes");
        }
        // end snippet
    }

    public static async Task Errors(IXapiClient client, Statement statement)
    {
        // snippet: client-errors
        try
        {
            await client.SaveStatementAsync(statement);
        }
        catch (XapiValidationException exception)
        {
            // Nothing was sent: the statement breaks a rule of the xAPI version in use
            foreach (var error in exception.Errors)
            {
                Console.WriteLine($"{error.Path}: {error.Message} ({error.Code})");
            }
        }
        catch (XapiConflictException)
        {
            // The LRS already has a different statement with this id
        }
        catch (XapiHttpException exception) when (exception.IsTransient)
        {
            // 408, 429 or 5xx: try again later, not before exception.RetryAfter if the LRS sent one
        }
        catch (XapiHttpException exception)
        {
            // Any other error status; the LRS usually explains it in the body
            Console.WriteLine($"{(int)exception.StatusCode}: {exception.ResponseBody}");
        }
        catch (HttpRequestException)
        {
            // No response: DNS, connection or TLS failure
        }
        // end snippet
    }

    internal interface ITokenSource
    {
        ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken);
    }
}
