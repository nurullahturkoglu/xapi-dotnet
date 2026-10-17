using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Client;
using Xapi.Client.Outbox;
using Xapi.Json;
using Xapi.Validation;

namespace Xapi.Documentation.Tests.Snippets;

// README.md and docs/getting-started.md. Compiled, not run: they talk to lrs.example.com or start a web server.
internal static class QuickStartSnippets
{
    public static async Task SendStatement(string key, string secret)
    {
        // snippet: send-statement
        using var http = new HttpClient();
        var client = new XapiClient(http, new XapiClientOptions
        {
            Endpoint = new Uri("https://lrs.example.com/xapi/"),
            Credentials = XapiCredentials.Basic(key, secret),
        });

        var statement = new Statement(
            Agent.FromMbox("learner@example.com", "Ada Learner"),
            Verbs.Completed,
            new Activity(new Iri("https://example.com/courses/fire-safety")));

        var id = await client.SaveStatementAsync(statement);
        Console.WriteLine($"The LRS stored statement {id}");
        // end snippet

        // snippet: read-statement
        var stored = await client.GetStatementAsync(id);
        if (stored is not null)
        {
            Console.WriteLine(XapiJson.SerializeStatement(stored, await client.GetVersionAsync()));
        }
        // end snippet
    }

    public static void HostLrs(string[] args)
    {
        // snippet: host-lrs
        var builder = WebApplication.CreateBuilder(args);

        builder.Services
            .AddXapiLrs(builder.Configuration.GetSection("Lrs"), options => options.MigrateOnStartup = true)
            .UseSqlite("Data Source=lrs.db");

        var app = builder.Build();
        app.MapXapiLrs("/xapi");
        app.Run();
        // end snippet
    }

    public static void SendFromAspNetCore(string[] args)
    {
        // snippet: client-di-outbox
        var builder = WebApplication.CreateBuilder(args);

        builder.Services
            .AddXapiClient(options =>
            {
                options.Endpoint = new Uri("https://lrs.example.com/xapi/");
                options.Credentials = XapiCredentials.Basic(builder.Configuration["Xapi:Key"]!, builder.Configuration["Xapi:Secret"]!);
            })
            .AddOutbox(Path.Combine(builder.Environment.ContentRootPath, "xapi-outbox"));

        var app = builder.Build();

        app.MapPost("/courses/{courseId}/complete", async (string courseId, string email, StatementOutbox outbox, CancellationToken cancellationToken) =>
        {
            var statement = new Statement(
                Agent.FromMbox(email),
                Verbs.Completed,
                new Activity(new Iri($"https://courses.example.com/{courseId}")));

            await outbox.EnqueueAsync(statement, cancellationToken);
            return Results.Accepted();
        });

        app.Run();
        // end snippet
    }

    public static void ReadAndValidate(string json)
    {
        // snippet: core-json
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
        // end snippet
    }
}
