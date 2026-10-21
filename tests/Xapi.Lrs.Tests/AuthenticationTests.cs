using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xapi.Lrs.Security;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.LrsApp;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

public class AuthenticationTests(LrsHosts hosts)
{
    public static TheoryData<string?> BadAuthorization => new()
    {
        { (string?)null },
        { Basic(Credentials.All, "wrong").ToString() },
        { Basic("nobody", "secret").ToString() },
        { "Basic not-base64!" },
        { "Basic " + Convert.ToBase64String("no-colon"u8.ToArray()) },
        { "Bearer abc" },
    };

    [Theory]
    [MemberData(nameof(BadAuthorization))]
    public async Task Requests_without_valid_credentials_get_401_with_a_basic_challenge(string? authorization)
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var client = app.Anonymous();
        if (authorization is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);
        }

        using var response = await client.PostStatementsAsync(Statement());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.Equal("2.0.0", Header(response, "X-Experience-API-Version"));
    }

    [Fact]
    public async Task Scopes_limit_what_a_credential_can_do()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var id = Guid.NewGuid();

        using var readerWrites = await app.Client(Credentials.Reader).PutStatementAsync(id, Statement());
        Assert.Equal(HttpStatusCode.Forbidden, readerWrites.StatusCode);

        using var writerWrites = await app.Client(Credentials.Writer).PutStatementAsync(id, Statement());
        Assert.Equal(HttpStatusCode.NoContent, writerWrites.StatusCode);

        using var writerReads = await app.Client(Credentials.Writer).GetStatementAsync(id);
        Assert.Equal(HttpStatusCode.Forbidden, writerReads.StatusCode);

        using var readerReads = await app.Client(Credentials.Reader).GetStatementAsync(id);
        Assert.Equal(HttpStatusCode.OK, readerReads.StatusCode);
    }

    [Fact]
    public async Task Read_mine_only_returns_statements_stored_with_the_same_credential()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        using (var put = await app.Client(Credentials.Mine).PutStatementAsync(mine, Statement()))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using (var put = await app.Client(Credentials.Writer).PutStatementAsync(other, Statement()))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using var own = await app.Client(Credentials.Mine).GetStatementAsync(mine);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var foreign = await app.Client(Credentials.Mine).GetStatementAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task The_authority_is_generated_from_the_credential_and_replaces_a_supplied_one()
    {
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var id = Guid.NewGuid();
        var statement = Statement();
        statement["authority"] = new JsonObject { ["mbox"] = "mailto:impostor@example.com" };

        using (var put = await app.Client(Credentials.Writer).PutStatementAsync(id, statement))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        var stored = await app.Client().GetStoredAsync(id);
        Assert.Equal(
            """{"objectType":"Agent","account":{"homePage":"https://lrs.example.com/","name":"writer"}}""",
            stored["authority"]!.ToJsonString());
    }

    [Fact]
    public async Task A_credential_added_with_an_authority_stores_statements_with_it()
    {
        const string Key = "lms";
        var app = await hosts.CreateAsync(TestDatabase.Sqlite, services => services.AddXapiLrs(o =>
            o.Credentials.Add(Key, Credentials.SecretOf(Key), Agent.FromAccount(new Uri("https://lms.example.com/"), "lms-service"), XapiScopes.StatementsWrite)));
        var id = Guid.NewGuid();

        using (var put = await app.Client(Key).PutStatementAsync(id, Statement()))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        var stored = await app.Client().GetStoredAsync(id);
        Assert.Equal(
            """{"objectType":"Agent","account":{"homePage":"https://lms.example.com/","name":"lms-service"}}""",
            stored["authority"]!.ToJsonString());
    }

    [Fact]
    public async Task A_delegated_scheme_supplies_the_identity_and_scopes()
    {
        var app = await hosts.CreateAsync(TestDatabase.Sqlite, services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, null);
            services.AddXapiLrs(o => o.AuthenticationScheme = HeaderAuthenticationHandler.SchemeName);
        });
        var id = Guid.NewGuid();

        using (var anonymous = await app.Anonymous().PutStatementAsync(id, Statement()))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        // Basic credentials are not used when a scheme is delegated to
        using (var basic = await app.Client().PutStatementAsync(id, Statement()))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, basic.StatusCode);
        }

        using (var noScope = await HeaderClient(app, "alice", "statements/read").PutStatementAsync(id, Statement()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noScope.StatusCode);
        }

        using (var put = await HeaderClient(app, "alice", "statements/write statements/read").PutStatementAsync(id, Statement()))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        var stored = await HeaderClient(app, "bob", "statements/read").GetStoredAsync(id);
        Assert.Equal(
            """{"objectType":"Agent","account":{"homePage":"https://lrs.example.com/","name":"alice"}}""",
            stored["authority"]!.ToJsonString());
    }

    [Fact]
    public async Task An_unknown_key_takes_as_long_to_reject_as_a_wrong_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        var store = app.Services.GetRequiredService<CredentialStore>();
        Assert.Null(await store.ValidateAsync("nobody", "secret", ct));
        Assert.Null(await store.ValidateAsync(Credentials.All, "wrong", ct));

        // The fastest of a few runs: PBKDF2 sets the floor, scheduling noise only adds to it. The runs alternate, so
        // a slow stretch on a shared CI machine lands on both sides. Without the check against a placeholder hash,
        // an unknown key is rejected after the database lookup alone, well over ten times faster.
        var unknown = TimeSpan.MaxValue;
        var wrong = TimeSpan.MaxValue;
        for (var i = 0; i < 10; i++)
        {
            unknown = Min(unknown, await TimeAsync(() => store.ValidateAsync("nobody", "secret", ct)));
            wrong = Min(wrong, await TimeAsync(() => store.ValidateAsync(Credentials.All, "wrong", ct)));
        }

        Assert.True(unknown >= wrong / 2, $"Unknown key: {unknown.TotalMilliseconds} ms, wrong secret: {wrong.TotalMilliseconds} ms.");
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task> action)
    {
        var started = Stopwatch.GetTimestamp();
        await action();
        return Stopwatch.GetElapsedTime(started);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static HttpClient HeaderClient(LrsApp app, string user, string scopes)
    {
        var client = app.Anonymous();
        client.DefaultRequestHeaders.Add(HeaderAuthenticationHandler.UserHeader, user);
        client.DefaultRequestHeaders.Add(HeaderAuthenticationHandler.ScopeHeader, scopes);
        return client;
    }

    // Stands in for an application's own scheme (JWT bearer, cookies, ...): trusts two request headers.
    private sealed class HeaderAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestHeader";
        public const string UserHeader = "X-Test-User";
        public const string ScopeHeader = "X-Test-Scope";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim("sub", user.ToString()), new Claim("scope", Request.Headers[ScopeHeader].ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
