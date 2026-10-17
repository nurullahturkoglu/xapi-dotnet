using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xapi.Lrs.Storage;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.DocumentApi;
using static Xapi.Lrs.Tests.Infrastructure.LrsApp;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

public class StorageTests(LrsHosts hosts)
{
    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Link_rows_cover_every_actor_and_activity_including_group_members_and_the_substatement(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var m = Enumerable.Range(0, 10).Select(_ => NewMbox()).ToArray();
        var a = Enumerable.Range(0, 6).Select(_ => NewIri()).ToArray();
        var statement = JsonNode.Parse($$$"""
            {
              "actor": {"objectType": "Group", "mbox": "{{{m[0]}}}", "member": [{"mbox": "{{{m[1]}}}"}]},
              "verb": {"id": "{{{Attempted}}}"},
              "object": {
                "objectType": "SubStatement",
                "actor": {"mbox": "{{{m[2]}}}"},
                "verb": {"id": "{{{Attempted}}}"},
                "object": {"id": "{{{a[0]}}}"},
                "context": {"instructor": {"mbox": "{{{m[3]}}}"}, "contextActivities": {"parent": [{"id": "{{{a[1]}}}"}]}}
              },
              "context": {
                "instructor": {"mbox": "{{{m[4]}}}"},
                "team": {"objectType": "Group", "member": [{"mbox": "{{{m[5]}}}"}, {"mbox": "{{{m[6]}}}"}]},
                "contextAgents": [{"objectType": "contextAgent", "agent": {"mbox": "{{{m[7]}}}"}}],
                "contextGroups": [{"objectType": "contextGroup", "group": {"objectType": "Group", "mbox": "{{{m[8]}}}"}}],
                "contextActivities": {
                  "parent": [{"id": "{{{a[2]}}}"}], "grouping": [{"id": "{{{a[3]}}}"}],
                  "category": [{"id": "{{{a[4]}}}"}], "other": [{"id": "{{{a[5]}}}"}]
                }
              }
            }
            """)!;
        var id = Guid.NewGuid();
        using (var put = await app.Client().PutStatementAsync(id, statement))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using var context = app.CreateDbContext();
        var pk = await context.Statements.Where(s => s.StatementId == id).Select(s => s.Pk).SingleAsync(TestContext.Current.CancellationToken);
        var actors = await context.StatementActors.Where(r => r.StatementPk == pk)
            .Select(r => new { r.Usage, r.ActorIfi, r.ActorType }).ToListAsync(TestContext.Current.CancellationToken);
        var activities = await context.StatementActivities.Where(r => r.StatementPk == pk)
            .Select(r => new { r.Usage, r.ActivityIri }).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            new[]
            {
                (1, "mbox:" + m[0], 2), (1, "mbox:" + m[1], 1), (101, "mbox:" + m[2], 1), (104, "mbox:" + m[3], 1),
                (3, $"account:{AuthorityHomePage} {Credentials.All}", 1), (4, "mbox:" + m[4], 1), (5, "mbox:" + m[5], 1),
                (5, "mbox:" + m[6], 1), (6, "mbox:" + m[7], 1), (7, "mbox:" + m[8], 2),
            }.Order(),
            actors.Select(r => ((int)r.Usage, r.ActorIfi, (int)r.ActorType)).Order());
        Assert.Equal(
            new[] { (101, a[0]), (102, a[1]), (2, a[2]), (3, a[3]), (4, a[4]), (5, a[5]) }.Order(),
            activities.Select(r => ((int)r.Usage, r.ActivityIri)).Order());

        // Identified Groups and Agents get actor rows; the anonymous team does not
        var ifis = m.Select(x => "mbox:" + x).ToList();
        var actorRows = await context.Actors.Where(r => ifis.Contains(r.Ifi)).Select(r => r.Ifi).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ifis.Take(9).Order(StringComparer.Ordinal), actorRows.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task The_primary_key_is_a_uuid_v7_carrying_the_stored_time(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var id = Guid.NewGuid();
        using (var put = await app.Client().PutStatementAsync(id, Statement()))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using var context = app.CreateDbContext();
        var row = await context.Statements.SingleAsync(s => s.StatementId == id, TestContext.Current.CancellationToken);
        Assert.Equal(7, row.Pk.Version);
        Assert.Equal(UuidV7Generator.GetTimestamp(row.Pk), row.Stored);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Non_ascii_text_is_stored_as_text_and_returned_byte_for_byte(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        const string Name = "Öğrenci 学生 \U0001F393 \u2028 \"quoted\"";
        var verb = NewIri("verbs");
        var statement = Statement(verb: verb);
        statement["actor"]!["name"] = Name;
        var id = Guid.NewGuid();
        using (var put = await client.PutStatementAsync(id, statement))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using var single = await client.GetStatementAsync(id);
        var exact = await single.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Name, JsonNode.Parse(exact)!["actor"]!["name"]!.GetValue<string>());

        // The payload column holds the same JSON as text (ADR-0011), and queries return it unchanged
        using var context = app.CreateDbContext();
        var payload = await context.Statements.Where(s => s.StatementId == id).Select(s => s.Payload).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Encoding.UTF8.GetString(exact), payload);
        using var query = await client.GetStatementsAsync(QueryApi.Query(("verb", verb)));
        var page = await query.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal($$"""{"statements":[{{payload}}],"more":""}""", page);

        var canonical = await client.QueryAsync(QueryApi.Query(("verb", verb), ("format", "canonical")));
        Assert.Equal(Name, canonical["statements"]![0]!["actor"]!["name"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Activity_definitions_merge_and_need_the_define_scope(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var iri = NewIri();

        async Task PostAsync(string key, JsonObject definition)
        {
            var statement = Statement(activity: iri);
            statement["object"]!["definition"] = definition;
            using var response = await app.Client(key).PostStatementsAsync(statement);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await PostAsync(Credentials.All, new JsonObject { ["name"] = new JsonObject { ["en-US"] = "Quiz" }, ["type"] = "http://example.com/types/a" });
        await PostAsync(Credentials.All, new JsonObject { ["name"] = new JsonObject { ["de"] = "Test" }, ["type"] = "http://example.com/types/b" });
        await PostAsync(Credentials.Writer, new JsonObject { ["name"] = new JsonObject { ["fr"] = "Examen" } });

        using var context = app.CreateDbContext();
        var payload = await context.Activities.Where(r => r.Iri == iri).Select(r => r.Payload).SingleAsync(TestContext.Current.CancellationToken);
        var definition = JsonNode.Parse(payload)!["definition"]!;
        Assert.Equal("""{"en-US":"Quiz","de":"Test"}""", definition["name"]!.ToJsonString());
        Assert.Equal("http://example.com/types/b", definition["type"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Actor_rows_keep_the_latest_name_from_credentials_with_the_define_scope(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var mbox = NewMbox();

        async Task PostAsync(string key, string name)
        {
            var statement = Statement(mbox: mbox);
            statement["actor"]!["name"] = name;
            using var response = await app.Client(key).PostStatementsAsync(statement);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await PostAsync(Credentials.Writer, "Ignored");
        await PostAsync(Credentials.All, "First");
        await PostAsync(Credentials.All, "Second");
        await PostAsync(Credentials.Writer, "Ignored again");

        using var context = app.CreateDbContext();
        var payload = await context.Actors.Where(r => r.Ifi == "mbox:" + mbox).Select(r => r.Payload).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Second", JsonNode.Parse(payload)!["name"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Statement_refs_record_every_statement_reached_in_any_arrival_order(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var client = app.Client();
        Guid s1 = Guid.NewGuid(), s2 = Guid.NewGuid(), s3 = Guid.NewGuid();

        JsonObject Ref(Guid target)
        {
            var statement = Statement();
            statement["object"] = new JsonObject { ["objectType"] = "StatementRef", ["id"] = target.ToString() };
            return statement;
        }

        // s3 -> s2 -> s1, with s2 arriving last
        foreach (var (id, statement) in new[] { (s3, Ref(s2)), (s1, Statement()), (s2, Ref(s1)) })
        {
            using var put = await client.PutStatementAsync(id, statement);
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        using var context = app.CreateDbContext();
        async Task<List<Guid>> ReachedAsync(Guid id) => await context.StatementRefs
            .Where(r => context.Statements.Any(s => s.StatementId == id && s.Pk == r.AncestorPk))
            .Select(r => r.DescendantId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { s1, s2 }.Order(), (await ReachedAsync(s3)).Order());
        Assert.Equal([s1], await ReachedAsync(s2));
        Assert.Empty(await ReachedAsync(s1));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task A_failure_part_way_through_a_batch_rolls_all_of_it_back(TestDatabase database)
    {
        var app = await hosts.CreateAsync(database, services =>
        {
            var inner = (XapiLrsDatabaseProvider)services.Single(d => d.ServiceType == typeof(XapiLrsDatabaseProvider)).ImplementationInstance!;
            services.Replace(ServiceDescriptor.Singleton<XapiLrsDatabaseProvider>(new FailingProvider(inner)));
        });
        var client = app.Client();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var voided = Guid.NewGuid();
        using (var put = await client.PutStatementAsync(voided, Statement()))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        // The statements and their link rows are written and the voiding applied before the activity rows fail
        using var response = await client.PostStatementsAsync(new JsonArray(
            Statement(first), Voiding(voided), Statement(second, activity: FailingProvider.FailingIri)));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        // Only the statement stored before the batch is left: one statement, its actor and authority rows and
        // its activity
        var ct = TestContext.Current.CancellationToken;
        using var context = app.CreateDbContext();
        Assert.Equal(voided, await context.Statements.Select(s => s.StatementId).SingleAsync(ct));
        Assert.False(await context.Statements.Select(s => s.IsVoided).SingleAsync(ct));
        Assert.Equal(2, await context.StatementActors.CountAsync(ct));
        Assert.Equal(1, await context.StatementActivities.CountAsync(ct));
        Assert.Equal(1, await context.Activities.CountAsync(ct));
        Assert.Empty(await context.StatementRefs.ToListAsync(ct));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Values_too_long_for_a_database_index_get_400_not_500(TestDatabase database)
    {
        // PostgreSQL rejects index rows over about 2.7 KB; SQLite has no such limit. Random text, because
        // PostgreSQL compresses index values that compress well.
        var client = (await hosts.SharedAsync(database)).Client();
        var text = string.Concat(Enumerable.Range(0, 125).Select(_ => Guid.NewGuid().ToString("N")));
        var expected = database == TestDatabase.PostgreSql ? HttpStatusCode.BadRequest : HttpStatusCode.OK;

        using (var activity = await client.PostStatementsAsync(Statement(activity: "http://example.com/" + text)))
        {
            Assert.Equal(expected, activity.StatusCode);
        }

        using (var mbox = await client.PostStatementsAsync(Statement(mbox: $"mailto:{text}@example.com")))
        {
            Assert.Equal(expected, mbox.StatusCode);
        }

        using var document = await client.PutDocAsync(DocTarget.New(DocumentResource.State).WithId(text), "x", "text/plain", ifNoneMatch: "*");
        Assert.Equal(database == TestDatabase.PostgreSql ? HttpStatusCode.BadRequest : HttpStatusCode.NoContent, document.StatusCode);
        Assert.Equal("2.0.0", Header(document, "X-Experience-API-Version"));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Writes_are_retried_after_conflicts_but_not_after_waiting_for_a_lock(TestDatabase database)
    {
        var provider = (await hosts.SharedAsync(database)).Services.GetRequiredService<XapiLrsDatabaseProvider>();

        if (database == TestDatabase.Sqlite)
        {
            // SQLITE_BUSY and SQLITE_LOCKED come after the 30-second wait for the lock; a retry would wait again
            Assert.False(provider.IsTransientWriteFailure(new DbUpdateException("busy", new SqliteException("database is locked", 5))));
            Assert.False(provider.IsTransientWriteFailure(new SqliteException("database table is locked", 6)));
            Assert.True(provider.IsTransientWriteFailure(new SqliteException("UNIQUE constraint failed", 19, 2067)));
        }
        else
        {
            Assert.True(provider.IsTransientWriteFailure(new DbUpdateException("conflict", new PostgresException("could not serialize access", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure))));
            Assert.True(provider.IsTransientWriteFailure(new PostgresException("deadlock detected", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected)));
            Assert.False(provider.IsTransientWriteFailure(new PostgresException("syntax error", "ERROR", "ERROR", PostgresErrorCodes.SyntaxError)));
        }
    }

    // Wraps the configured provider and fails SaveChanges once it holds the activity row of FailingIri.
    private sealed class FailingProvider(XapiLrsDatabaseProvider inner) : XapiLrsDatabaseProvider
    {
        public const string FailingIri = "http://example.com/activities/fail-here";

        public override string Name => inner.Name;

        public override void Configure(DbContextOptionsBuilder optionsBuilder)
        {
            inner.Configure(optionsBuilder);
            optionsBuilder.AddInterceptors(new FailOnActivity());
        }

        public override Task<IDbContextTransaction> BeginWriteTransactionAsync(DbContext context, CancellationToken cancellationToken) =>
            inner.BeginWriteTransactionAsync(context, cancellationToken);

        public override bool IsTransientWriteFailure(Exception exception) => inner.IsTransientWriteFailure(exception);

        private sealed class FailOnActivity : SaveChangesInterceptor
        {
            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
                eventData.Context!.ChangeTracker.Entries<ActivityEntity>().Any(e => e.Entity.Iri == FailingIri)
                    ? throw new InvalidOperationException("Injected failure")
                    : base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
