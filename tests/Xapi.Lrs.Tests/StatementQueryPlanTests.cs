using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.QueryApi;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

// The database plans of statement queries: a filter is read from its index, whether it matches many statements or
// none, instead of walking every statement newest first and testing each one.
public partial class StatementQueryPlanTests(LrsHosts hosts)
{
    private const int Stored = 1000;

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Filters_that_match_nothing_read_their_index_instead_of_every_statement(TestDatabase database)
    {
        var recorder = new PlanRecorder();
        var app = await hosts.CreateAsync(database, services => Interception.Add(services, new Explainer(recorder)));
        var client = app.Client();
        await StoreAsync(client);
        if (database == TestDatabase.PostgreSql)
        {
            // Statistics, as autovacuum would have them on a database in use
            await using var context = app.CreateDbContext();
            await context.Database.ExecuteSqlRawAsync("ANALYZE", TestContext.Current.CancellationToken);
        }

        var nobody = new JsonObject { ["mbox"] = NewMbox() }.ToJsonString();
        (string Name, string Value)[][] queries =
        [
            [("verb", NewIri("verbs"))],
            [("activity", NewIri())],
            [("activity", NewIri()), ("related_activities", "true")],
            [("agent", nobody)],
            [("agent", nobody), ("related_agents", "true")],
            [("registration", Guid.NewGuid().ToString())],
            [("verb", Attempted), ("agent", nobody)],
        ];
        foreach (var query in queries)
        {
            recorder.Plans.Clear();
            Assert.Empty(Ids(await client.QueryAsync(Query(query))));

            var plan = Assert.Single(recorder.Plans);
            var description = string.Join(", ", query.Select(p => p.Name));
            if (database == TestDatabase.Sqlite)
            {
                // SQLite has no statistics here, so the plan does not depend on the data: no table of statements
                // or link rows may be scanned
                Assert.True(plan.FullScans.Count == 0, $"{description}: {string.Join("; ", plan.FullScans)}\n{plan.Text}");
            }
            else
            {
                Assert.True(plan.RowsRead < 50, $"{description}: {plan.RowsRead} rows read\n{plan.Text}");
            }
        }
    }

    // A filter that matches a few statements, in a database with more StatementRefs than that: the matches
    // through StatementRefs are read from the filter's index too, not from every xapi_statement_ref row
    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task StatementRef_matches_of_a_filter_with_few_matches_are_read_from_its_index(TestDatabase database)
    {
        var recorder = new PlanRecorder();
        var app = await hosts.CreateAsync(database, services => Interception.Add(services, new Explainer(recorder)));
        var client = app.Client();
        await StoreAsync(client);
        var verb = NewIri("verbs");
        var target = await client.PutAllAsync(Statement(verb: verb));
        var reference = await client.PutAllAsync(Referencing(target[0]));
        if (database == TestDatabase.PostgreSql)
        {
            await using var context = app.CreateDbContext();
            await context.Database.ExecuteSqlRawAsync("ANALYZE", TestContext.Current.CancellationToken);
        }

        recorder.Plans.Clear();
        Assert.Equal([reference[0], target[0]], Ids(await client.QueryAsync(Query(("verb", verb)))));

        var plan = Assert.Single(recorder.Plans);
        if (database == TestDatabase.Sqlite)
        {
            Assert.True(plan.FullScans.Count == 0, $"{string.Join("; ", plan.FullScans)}\n{plan.Text}");
        }
        else
        {
            Assert.True(plan.RowsRead < 50, $"{plan.RowsRead} rows read\n{plan.Text}");
        }
    }

    // Statements with a verb, an activity, a learner, an instructor, a parent and a registration each, so every
    // filter has rows to walk past. Every tenth is a StatementRef to the statement before it instead.
    private static async Task StoreAsync(HttpClient client)
    {
        for (var start = 0; start < Stored; start += 100)
        {
            var batch = new JsonArray();
            var previous = Guid.Empty;
            for (var n = start; n < start + 100; n++)
            {
                var id = Guid.NewGuid();
                if (n % 10 == 9)
                {
                    batch.Add(Referencing(previous, id));
                }
                else
                {
                    var statement = Statement(id, activity: $"http://example.com/activities/{n % 40}", mbox: $"mailto:learner{n % 50}@example.com");
                    statement["context"] = new JsonObject
                    {
                        ["registration"] = Guid.NewGuid().ToString(),
                        ["instructor"] = new JsonObject { ["mbox"] = "mailto:instructor@example.com" },
                        ["contextActivities"] = new JsonObject { ["parent"] = new JsonArray(new JsonObject { ["id"] = "http://example.com/course" }) },
                    };
                    batch.Add(statement);
                }

                previous = id;
            }

            using var response = await client.PostStatementsAsync(batch);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
    }

    private sealed record Plan(string Text, IReadOnlyList<string> FullScans, double RowsRead);

    private sealed class PlanRecorder
    {
        public ConcurrentQueue<Plan> Plans { get; } = new();
    }

    // Explains every statement query before it runs
    private sealed partial class Explainer(PlanRecorder recorder) : DbCommandInterceptor
    {
        // The tables a filter may only read through an index range
        private static readonly string[] Guarded = ["xapi_statement", "xapi_statement_actor", "xapi_statement_activity"];

        // On SQLite also xapi_statement_ref: every filter here matches fewer statements than there are StatementRefs,
        // and the store then reads the StatementRef matches from the filter's index (StatementRefMatchesAsync).
        // PostgreSQL chooses from its statistics and may read the 100 StatementRef rows instead.
        private static readonly string[] ScanGuarded = [.. Guarded, "xapi_statement_ref"];

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            // The page query of QueryAsync: the only one that reads payloads in key order
            if (command.CommandText.Contains("AS \"Payload\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("ORDER BY", StringComparison.Ordinal))
            {
                recorder.Plans.Enqueue(command is SqliteCommand
                    ? await ExplainSqliteAsync(command, cancellationToken)
                    : await ExplainPostgreSqlAsync(command, cancellationToken));
            }

            return result;
        }

        private static async Task<Plan> ExplainSqliteAsync(DbCommand command, CancellationToken cancellationToken)
        {
            await using var explain = Copy(command, "EXPLAIN QUERY PLAN ");
            var details = new List<string>();
            await using (var reader = await explain.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    details.Add(reader.GetString(3));
                }
            }

            var guardedAliases = TableAlias().Matches(command.CommandText)
                .Where(m => ScanGuarded.Contains(m.Groups["table"].Value))
                .Select(m => m.Groups["alias"].Value)
                .ToHashSet();
            var fullScans = details.Where(d => ScanOf().Match(d) is { Success: true } scan && guardedAliases.Contains(scan.Groups["alias"].Value)).ToList();
            return new Plan(string.Join("\n", details), fullScans, double.NaN);
        }

        private static async Task<Plan> ExplainPostgreSqlAsync(DbCommand command, CancellationToken cancellationToken)
        {
            await using var explain = Copy(command, "EXPLAIN (ANALYZE, FORMAT JSON) ");
            var json = (string)(await explain.ExecuteScalarAsync(cancellationToken))!;
            using var document = JsonDocument.Parse(json);
            return new Plan(json, [], RowsRead(document.RootElement[0].GetProperty("Plan")));
        }

        // Rows that scans of the guarded tables returned or filtered out, over all loops
        private static double RowsRead(JsonElement node)
        {
            var rows = 0.0;
            if (node.TryGetProperty("Relation Name", out var relation) && Guarded.Contains(relation.GetString()))
            {
                var loops = node.GetProperty("Actual Loops").GetDouble();
                rows += loops * node.GetProperty("Actual Rows").GetDouble();
                if (node.TryGetProperty("Rows Removed by Filter", out var removed))
                {
                    rows += loops * removed.GetDouble();
                }
            }

            if (node.TryGetProperty("Plans", out var children))
            {
                rows += children.EnumerateArray().Sum(RowsRead);
            }

            return rows;
        }

        private static DbCommand Copy(DbCommand command, string prefix)
        {
            var copy = command.Connection!.CreateCommand();
            copy.Transaction = command.Transaction;
            copy.CommandText = prefix + command.CommandText;
            foreach (DbParameter parameter in command.Parameters)
            {
                copy.Parameters.Add(parameter switch
                {
                    SqliteParameter sqlite => new SqliteParameter(sqlite.ParameterName, sqlite.SqliteType) { Value = sqlite.Value },
                    NpgsqlParameter npgsql => npgsql.Clone(),
                    _ => throw new NotSupportedException(parameter.GetType().Name),
                });
            }

            return copy;
        }

        [GeneratedRegex("\"(?<table>xapi_[a-z_]+)\" AS \"(?<alias>[a-z0-9]+)\"")]
        private static partial Regex TableAlias();

        [GeneratedRegex("^SCAN (?<alias>[a-z0-9]+)(?: |$)")]
        private static partial Regex ScanOf();
    }
}
