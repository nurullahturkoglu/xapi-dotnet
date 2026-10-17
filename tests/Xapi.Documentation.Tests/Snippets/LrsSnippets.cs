using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Lrs;

namespace Xapi.Documentation.Tests.Snippets;

// docs/lrs.md. Compiled; LrsSnippetTests runs the migration step.
internal static class LrsSnippets
{
    public static void Options(WebApplicationBuilder builder)
    {
        // snippet: lrs-options
        builder.Services
            .AddXapiLrs(options =>
            {
                options.Credentials.Add("course-player", builder.Configuration["Lrs:PlayerSecret"]!,
                    XapiScope.StatementsWrite, XapiScope.StatementsReadMine, XapiScope.State);
                options.Credentials.Add("reporting", builder.Configuration["Lrs:ReportingSecret"]!, XapiScope.AllRead);
                options.Credentials.Add(new XapiCredential("lms", builder.Configuration["Lrs:LmsSecret"]!, [XapiScope.All])
                {
                    Authority = Agent.FromAccount(new Iri("https://lms.example.com/"), "lms"),
                });

                options.AuthorityHomePage = new Iri("https://lrs.example.com/");
                options.MaxDocumentBytes = 256 * 1024;
                options.MaxStatementsPerPage = 500;
                options.MigrateOnStartup = true;
            })
            .UseSqlite(builder.Configuration.GetConnectionString("Lrs")!);
        // end snippet
    }

    public static void PostgreSql(WebApplicationBuilder builder)
    {
        // snippet: lrs-postgresql
        builder.Services
            .AddXapiLrs(options => options.Credentials.Add("my-app", builder.Configuration["Lrs:Secret"]!, XapiScope.All))
            .UsePostgreSql(builder.Configuration.GetConnectionString("Lrs")!);
        // end snippet
    }

    public static void Delegated(WebApplicationBuilder builder)
    {
        // snippet: lrs-delegated
        // The app registers the scheme itself, for example with AddJwtBearer() from
        // Microsoft.AspNetCore.Authentication.JwtBearer
        builder.Services
            .AddXapiLrs(options =>
            {
                options.AuthenticationScheme = "Bearer";
                options.ScopeClaimType = "scope";
                options.AuthorityHomePage = new Iri("https://login.example.com/");
            })
            .UseSqlite("Data Source=lrs.db");
        // end snippet
    }

    public static void RateLimiting(WebApplicationBuilder builder)
    {
        // snippet: lrs-rate-limiting
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("xapi", limiter =>
            {
                limiter.PermitLimit = 100;
                limiter.Window = TimeSpan.FromSeconds(1);
            });
        });

        var app = builder.Build();
        app.UseRateLimiter();
        app.MapXapiLrs("/xapi").RequireRateLimiting("xapi");
        // end snippet
    }

    public static void Cors(WebApplicationBuilder builder)
    {
        // snippet: lrs-cors
        builder.Services.AddCors(options => options.AddPolicy("xapi", policy => policy
            .WithOrigins("https://course.example.com")
            .WithMethods("GET", "HEAD", "PUT", "POST", "DELETE")
            .WithHeaders("Authorization", "Content-Type", "X-Experience-API-Version", "If-Match", "If-None-Match")
            .WithExposedHeaders("ETag", "Last-Modified", "X-Experience-API-Version", "X-Experience-API-Consistent-Through")));

        var app = builder.Build();
        app.UseCors();
        app.MapXapiLrs("/xapi").RequireCors("xapi");
        // end snippet
    }

    public static async Task MigrateOnly(WebApplication app, string[] args)
    {
        // snippet: lrs-migrate
        if (args.Contains("--migrate"))
        {
            await app.Services.MigrateXapiLrsDatabaseAsync();
            return;
        }
        // end snippet

        await app.RunAsync();
    }
}
