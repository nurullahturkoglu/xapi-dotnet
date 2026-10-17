using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xapi.Lrs.Hosting;
using Xapi.Lrs.Tests.Infrastructure;

namespace Xapi.Lrs.Tests;

public class OptionsTests
{
    private const string Secret = "do-not-print-this-secret";

    [Fact]
    public async Task Startup_fails_with_every_validation_problem_listed()
    {
        var database = Path.Combine(Path.GetTempPath(), $"xapi-options-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddXapiLrs(o =>
            {
                o.MigrateOnStartup = true;
                o.ScopeClaimType = " ";
            })
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        await using var app = builder.Build();

        try
        {
            var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));

            Assert.Equal(2, exception.Failures.Count());
            Assert.Contains("add a credential to Credentials", exception.Message, StringComparison.Ordinal);
            Assert.Contains("ScopeClaimType is empty", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(database), "Validation runs before the database is touched.");
        }
        finally
        {
            File.Delete(database);
        }
    }

    [Theory]
    [InlineData("", "AuthenticationScheme is empty")]
    [InlineData(XapiLrsAuthenticationDefaults.AuthenticationScheme, "built-in Basic scheme")]
    public void A_scheme_that_cannot_replace_basic_authentication_is_rejected(string scheme, string expected)
    {
        var options = new XapiLrsOptions { AuthenticationScheme = scheme };
        options.Credentials.Add("key", Secret, XapiScopes.All);

        var result = new XapiLrsOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(expected, result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "AuthorityHomePage is null or empty")]
    [InlineData("lrs.example.com", "AuthorityHomePage 'lrs.example.com' is not an absolute URI")]
    [InlineData("/credentials", "AuthorityHomePage '/credentials' is not an absolute URI")]
    public void A_missing_or_relative_authority_home_page_is_rejected(string? homePage, string expected)
    {
        var options = new XapiLrsOptions { AuthorityHomePage = homePage is null ? null! : new Uri(homePage, UriKind.Relative) };
        options.Credentials.Add("key", Secret, XapiScopes.All);

        var result = new XapiLrsOptionsValidator().Validate(Options.DefaultName, options);

        Assert.Contains(expected, Assert.Single(result.Failures!), StringComparison.Ordinal);
    }

    [Fact]
    public void Credentials_or_a_scheme_are_enough()
    {
        var basic = new XapiLrsOptions();
        basic.Credentials.Add("key", Secret, XapiScopes.All);

        Assert.True(new XapiLrsOptionsValidator().Validate(Options.DefaultName, basic).Succeeded);
        Assert.True(new XapiLrsOptionsValidator().Validate(Options.DefaultName, new XapiLrsOptions { AuthenticationScheme = "Bearer" }).Succeeded);
    }

    [Theory]
    [InlineData(nameof(XapiLrsOptions.MaxDocumentBytes), 1)]
    [InlineData(nameof(XapiLrsOptions.MaxStatementBytes), 1)]
    [InlineData(nameof(XapiLrsOptions.MaxAttachmentBytes), 0)]
    [InlineData(nameof(XapiLrsOptions.MaxStatementsPerBatch), 1)]
    [InlineData(nameof(XapiLrsOptions.MaxAlternateRequestBytes), 1)]
    [InlineData(nameof(XapiLrsOptions.MaxStatementsPerPage), 1)]
    public void Limits_below_their_minimum_are_rejected(string option, int minimum)
    {
        var options = new XapiLrsOptions();
        options.Credentials.Add("key", Secret, XapiScopes.All);
        var property = typeof(XapiLrsOptions).GetProperty(option)!;

        property.SetValue(options, Convert.ChangeType(minimum, property.PropertyType, null));
        Assert.True(new XapiLrsOptionsValidator().Validate(Options.DefaultName, options).Succeeded);

        property.SetValue(options, Convert.ChangeType(minimum - 1, property.PropertyType, null));
        var failure = Assert.Single(new XapiLrsOptionsValidator().Validate(Options.DefaultName, options).Failures!);
        Assert.Contains($"{option} is {minimum - 1}; it must be at least {minimum}", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, Secret, XapiScopes.All, "Credentials[0] has no Key")]
    [InlineData("", Secret, XapiScopes.All, "Credentials[0] has no Key")]
    [InlineData("a:b", Secret, XapiScopes.All, "Credentials[0] ('a:b') has a Key with a colon")]
    [InlineData("player", null, XapiScopes.All, "Credentials[0] ('player') has no Secret")]
    [InlineData("player", "", XapiScopes.All, "Credentials[0] ('player') has no Secret")]
    [InlineData("player", Secret, null, "Credentials[0] ('player') has no Scopes")]
    [InlineData("player", Secret, "statements/writ", "Credentials[0] ('player') has an unknown scope 'statements/writ'")]
    [InlineData("player", Secret, "Statements/Write", "unknown scope 'Statements/Write'")]
    // The names, numbers and comma-separated combinations of the former enum: the configuration binder's enum
    // converter would have ORed "StatementsWrite,StatementsReadMine" into StatementsRead
    [InlineData("player", Secret, "StatementsWrite", "unknown scope 'StatementsWrite'")]
    [InlineData("player", Secret, "StatementsWrite,StatementsReadMine", "unknown scope 'StatementsWrite,StatementsReadMine'")]
    [InlineData("player", Secret, "8", "unknown scope '8'")]
    [InlineData("player", Secret, "all/read state", "unknown scope 'all/read state'")]
    [InlineData("player", Secret, "", "unknown scope ''")]
    public void An_invalid_credential_is_rejected_without_its_secret(string? key, string? secret, string? scope, string expected)
    {
        var options = new XapiLrsOptions();
        var credential = new XapiLrsCredential { Key = key, Secret = secret };
        if (scope is not null)
        {
            credential.Scopes.Add(scope);
        }

        options.Credentials.Add(credential);

        var result = new XapiLrsOptionsValidator().Validate(Options.DefaultName, options);

        Assert.Contains(expected, Assert.Single(result.Failures!), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_a_credential_does_not_check_it_and_the_validator_lists_every_problem()
    {
        var options = new XapiLrsOptions();
        options.Credentials.Add("player", Secret, XapiScopes.StatementsWrite, XapiScopes.State);
        options.Credentials.Add("reporting", Secret, XapiScopes.AllRead);
        options.Credentials.Add("player", Secret, XapiScopes.All);
        options.Credentials.Add("a:b", "", "statements/writ");

        var result = new XapiLrsOptionsValidator().Validate(Options.DefaultName, options);

        Assert.Equal(
            [
                "xAPI LRS options: Credentials[2] ('player') has the same Key as Credentials[0].",
                "xAPI LRS options: Credentials[3] ('a:b') has a Key with a colon, which Basic authentication cannot send.",
                "xAPI LRS options: Credentials[3] ('a:b') has no Secret.",
                "xAPI LRS options: Credentials[3] ('a:b') has an unknown scope 'statements/writ'. Use one xAPI scope name per entry, such as statements/write (see XapiScopes).",
            ],
            result.Failures!);
        Assert.Throws<ArgumentNullException>(() => options.Credentials.Add(null!));
    }

    [Fact]
    public void The_configuration_overload_binds_every_option_and_the_credentials()
    {
        var options = OptionsFrom(services => services.AddXapiLrs(Configuration(new()
        {
            ["Lrs:AuthenticationScheme"] = "Bearer",
            ["Lrs:ScopeClaimType"] = "scp",
            ["Lrs:AuthorityHomePage"] = "https://lrs.example.com/",
            ["Lrs:EnableVersion1_0_3"] = "false",
            ["Lrs:MaxDocumentBytes"] = "2048",
            ["Lrs:MaxStatementBytes"] = "4096",
            ["Lrs:MaxAttachmentBytes"] = "0",
            ["Lrs:MaxStatementsPerBatch"] = "10",
            ["Lrs:MaxAlternateRequestBytes"] = "8192",
            ["Lrs:MaxStatementsPerPage"] = "20",
            ["Lrs:MigrateOnStartup"] = "true",
            ["Lrs:Credentials:0:Key"] = "player",
            ["Lrs:Credentials:0:Secret"] = Secret,
            ["Lrs:Credentials:0:Scopes:0"] = "statements/write",
            ["Lrs:Credentials:0:Scopes:1"] = "state",
            ["Lrs:Credentials:1:Key"] = "reporting",
            ["Lrs:Credentials:1:Secret"] = Secret,
            ["Lrs:Credentials:1:Scopes:0"] = "all/read",
        })));

        Assert.Equal("Bearer", options.AuthenticationScheme);
        Assert.Equal("scp", options.ScopeClaimType);
        Assert.Equal(new Uri("https://lrs.example.com/"), options.AuthorityHomePage);
        Assert.False(options.EnableVersion1_0_3);
        Assert.Equal(2048, options.MaxDocumentBytes);
        Assert.Equal(4096, options.MaxStatementBytes);
        Assert.Equal(0, options.MaxAttachmentBytes);
        Assert.Equal(10, options.MaxStatementsPerBatch);
        Assert.Equal(8192, options.MaxAlternateRequestBytes);
        Assert.Equal(20, options.MaxStatementsPerPage);
        Assert.True(options.MigrateOnStartup);
        Assert.Collection(
            options.Credentials,
            c =>
            {
                Assert.Equal(("player", Secret), (c.Key, c.Secret));
                Assert.Equal([XapiScopes.StatementsWrite, XapiScopes.State], c.Scopes);
            },
            c =>
            {
                Assert.Equal(("reporting", Secret), (c.Key, c.Secret));
                Assert.Equal([XapiScopes.AllRead], c.Scopes);
            });
    }

    [Fact]
    public void The_callback_runs_after_the_section_is_bound()
    {
        var lms = Agent.FromAccount(new Uri("https://lms.example.com/"), "lms");

        var options = OptionsFrom(services => services.AddXapiLrs(
            Configuration(new()
            {
                ["Lrs:MaxStatementsPerPage"] = "20",
                ["Lrs:Credentials:0:Key"] = "player",
                ["Lrs:Credentials:0:Secret"] = Secret,
                ["Lrs:Credentials:0:Scopes:0"] = "all",
            }),
            o =>
            {
                o.MaxStatementsPerPage = 7;
                o.Credentials.Add(new XapiLrsCredential("lms", Secret, XapiScopes.All) { Authority = lms });
            }));

        Assert.Equal(7, options.MaxStatementsPerPage);
        Assert.Equal(["player", "lms"], options.Credentials.Select(c => c.Key));
        Assert.Same(lms, options.Credentials[1].Authority);
    }

    [Fact]
    public void Configure_with_a_section_binds_the_options_too()
    {
        var options = OptionsFrom(services =>
        {
            services.AddXapiLrs();
            services.Configure<XapiLrsOptions>(Configuration(new()
            {
                ["Lrs:AuthorityHomePage"] = "https://lrs.example.com/",
                ["Lrs:Credentials:0:Key"] = "player",
                ["Lrs:Credentials:0:Secret"] = Secret,
                ["Lrs:Credentials:0:Scopes:0"] = "all",
                ["Lrs:Unknown"] = "ignored by default",
            }));
        });

        Assert.Equal(new Uri("https://lrs.example.com/"), options.AuthorityHomePage);
        Assert.Equal("player", Assert.Single(options.Credentials).Key);
    }

    [Theory]
    [InlineData("Lrs:Credentials:0:Secret", null, "Credentials[0] ('player') has no Secret")]
    [InlineData("Lrs:Credentials:0:Key", null, "Credentials[0] has no Key")]
    [InlineData("Lrs:Credentials:0:Key", "a:b", "Credentials[0] ('a:b') has a Key with a colon")]
    [InlineData("Lrs:Credentials:0:Scopes:0", null, "Credentials[0] ('player') has no Scopes")]
    [InlineData("Lrs:Credentials:0:Scopes:0", "statements/writ", "unknown scope 'statements/writ'")]
    [InlineData("Lrs:Credentials:0:Scopes:0", "StatementsWrite,StatementsReadMine", "unknown scope 'StatementsWrite,StatementsReadMine'")]
    [InlineData("Lrs:Credentials:0:Scopes:0", "8", "unknown scope '8'")]
    [InlineData("Lrs:Credentials:1:Key", "player", "Credentials[1] ('player') has the same Key as Credentials[0]")]
    // Misspelt keys: ErrorOnUnknownConfiguration names the key, not its value
    [InlineData("Lrs:Credentials:0:Secert", Secret, "'Secert'")]
    [InlineData("Lrs:MaxStatementPerPage", "20", "'MaxStatementPerPage'")]
    [InlineData("Lrs:Credentials:0:Authority:account:name", "lms", "Agent")]
    [InlineData("Lrs:MaxStatementBytes", "0", "MaxStatementBytes is 0")]
    [InlineData("Lrs:MaxStatementsPerPage", "many", "'Lrs:MaxStatementsPerPage'")]
    [InlineData("Lrs:EnableVersion1_0_3", "yes", "'Lrs:EnableVersion1_0_3'")]
    [InlineData("Lrs:AuthorityHomePage", "lrs.example.com", "AuthorityHomePage 'lrs.example.com' is not an absolute URI")]
    // The binder does not skip an empty value, such as an empty environment variable
    [InlineData("Lrs:MaxStatementsPerPage", "", "'Lrs:MaxStatementsPerPage'")]
    [InlineData("Lrs:EnableVersion1_0_3", "", "'Lrs:EnableVersion1_0_3'")]
    [InlineData("Lrs:AuthorityHomePage", "", "AuthorityHomePage is null or empty")]
    [InlineData("Lrs:AuthenticationScheme", "", "AuthenticationScheme is empty")]
    // A single value where the binder expects an array
    [InlineData("Lrs:Credentials:0:Scopes", "all/read statements/read", "'Lrs:Credentials:0:Scopes'")]
    public void Invalid_configuration_fails_with_a_message_without_the_secret(string key, string? value, string expected)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Lrs:Credentials:0:Key"] = "player",
            ["Lrs:Credentials:0:Secret"] = Secret,
            ["Lrs:Credentials:0:Scopes:0"] = "all",
        };
        settings[key] = value;
        if (key.StartsWith("Lrs:Credentials:1:", StringComparison.Ordinal))
        {
            settings["Lrs:Credentials:1:Secret"] = Secret;
            settings["Lrs:Credentials:1:Scopes:0"] = "all";
        }

        // A missing value is a missing key: the in-memory provider would otherwise keep a null entry
        if (value is null)
        {
            settings.Remove(key);
        }
        else if (key == "Lrs:Credentials:0:Scopes")
        {
            settings.Remove("Lrs:Credentials:0:Scopes:0");
        }

        // OptionsValidationException from the validator, InvalidOperationException from the binder
        var exception = Assert.ThrowsAny<Exception>(() => OptionsFrom(services => services.AddXapiLrs(Configuration(settings))));

        // The binder puts the details of a failure inside a credential in the inner exception
        Assert.Contains(expected, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_misspelt_key_stops_the_host_at_startup()
    {
        var database = Path.Combine(Path.GetTempPath(), $"xapi-options-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lrs:MigrateOnStartup"] = "true",
            ["Lrs:MaxStatementPerPage"] = "20",
            ["Lrs:Credentials:0:Key"] = "player",
            ["Lrs:Credentials:0:Secret"] = Secret,
            ["Lrs:Credentials:0:Scopes:0"] = "all",
        });
        builder.Services
            .AddXapiLrs(builder.Configuration.GetSection("Lrs"))
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        await using var app = builder.Build();

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(TestContext.Current.CancellationToken));

            Assert.Contains("'MaxStatementPerPage'", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(database), "Binding fails before the database is touched.");
        }
        finally
        {
            File.Delete(database);
        }
    }

    [Fact]
    public async Task Credentials_bound_from_configuration_authenticate_requests()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = Path.Combine(Path.GetTempPath(), $"xapi-options-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lrs:MigrateOnStartup"] = "true",
            ["Lrs:Credentials:0:Key"] = "reader",
            ["Lrs:Credentials:0:Secret"] = Secret,
            ["Lrs:Credentials:0:Scopes:0"] = "statements/read",
        });
        builder.Services
            .AddXapiLrs(builder.Configuration.GetSection("Lrs"))
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        await using var app = builder.Build();
        app.MapXapiLrs("/xapi");
        await app.StartAsync(ct);

        try
        {
            var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-Experience-API-Version", "2.0.0");
            client.DefaultRequestHeaders.Authorization = LrsApp.Basic("reader", Secret);

            using var read = await client.GetAsync(new Uri("/xapi/statements", UriKind.Relative), ct);
            using var write = await client.PostAsync(new Uri("/xapi/statements", UriKind.Relative), new StringContent("[]", null, "application/json"), ct);

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        }
        finally
        {
            await app.StopAsync(ct);
            File.Delete(database);
        }
    }

    // Binds and validates the options as the host does when it starts
    private static XapiLrsOptions OptionsFrom(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<XapiLrsOptions>>().Value;
    }

    private static IConfigurationSection Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build().GetSection("Lrs");
}
