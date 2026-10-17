using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
                    XapiScopes.StatementsWrite, XapiScopes.StatementsReadMine, XapiScopes.State);
                options.Credentials.Add("reporting", builder.Configuration["Lrs:ReportingSecret"]!, XapiScopes.AllRead);
                options.Credentials.Add(new XapiLrsCredential("lms", builder.Configuration["Lrs:LmsSecret"]!, XapiScopes.All)
                {
                    Authority = Agent.FromAccount(new Iri("https://lms.example.com/"), "lms"),
                });

                options.AuthorityHomePage = new Uri("https://lrs.example.com/");
                options.MaxDocumentBytes = 256 * 1024;
                options.MaxStatementsPerPage = 500;
                options.MigrateOnStartup = true;
            })
            .UseSqlite(builder.Configuration.GetConnectionString("Lrs")!);
        // end snippet
    }

    public static void FromConfiguration(WebApplicationBuilder builder)
    {
        // snippet: lrs-configuration
        builder.Services
            .AddXapiLrs(builder.Configuration.GetSection("Lrs"))
            .UseSqlite(builder.Configuration.GetConnectionString("Lrs")!);
        // end snippet
    }

    public static void PostgreSql(WebApplicationBuilder builder)
    {
        // snippet: lrs-postgresql
        builder.Services
            .AddXapiLrs(options => options.Credentials.Add("my-app", builder.Configuration["Lrs:Secret"]!, XapiScopes.All))
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
                options.AuthorityHomePage = new Uri("https://login.example.com/");
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

    public static void HealthCheck(WebApplicationBuilder builder)
    {
        // snippet: lrs-health
        builder.Services.AddHealthChecks().AddXapiLrsHealthCheck(tags: ["ready"]);

        var app = builder.Build();
        app.MapXapiLrs("/xapi");
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
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
