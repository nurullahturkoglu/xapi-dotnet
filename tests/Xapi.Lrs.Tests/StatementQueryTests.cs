using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.LrsApp;
using static Xapi.Lrs.Tests.Infrastructure.QueryApi;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

// GET /statements without statementId (IEEE 9274.1.1, 4.1.6.1). Tests on the shared LRSs filter on fresh
// IRIs and mailboxes, so statements stored by other tests do not show up.
public class StatementQueryTests(LrsHosts hosts)
{
    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Each_filter_returns_only_matching_statements_newest_first(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var verb = NewIri("verbs");
        var activity = NewIri();
        var mbox = NewMbox();
        var registration = Guid.NewGuid();

        var all = Statement(activity: activity, mbox: mbox, verb: verb);
        all["context"] = new JsonObject { ["registration"] = registration.ToString() };
        var agentAsObject = Statement(verb: verb);
        agentAsObject["object"] = new JsonObject { ["objectType"] = "Agent", ["mbox"] = mbox };
        var groupMember = Statement();
        groupMember["actor"] = new JsonObject { ["objectType"] = "Group", ["member"] = new JsonArray(new JsonObject { ["mbox"] = mbox }) };
        var activityOnly = Statement(activity: activity);
        var registrationOnly = Statement();
        registrationOnly["context"] = new JsonObject { ["registration"] = registration.ToString() };

        var ids = await client.PutAllAsync(all, agentAsObject, groupMember, activityOnly, registrationOnly);

        Assert.Equal([ids[2], ids[1], ids[0]], Ids(await client.QueryAsync(Query(("agent", $$"""{"mbox":"{{mbox}}"}""")))));
        Assert.Equal([ids[1], ids[0]], Ids(await client.QueryAsync(Query(("verb", verb)))));
        Assert.Equal([ids[3], ids[0]], Ids(await client.QueryAsync(Query(("activity", activity)))));
        Assert.Equal([ids[4], ids[0]], Ids(await client.QueryAsync(Query(("registration", registration.ToString())))));
        Assert.Equal([ids[0]], Ids(await client.QueryAsync(Query(("agent", $$"""{"mbox":"{{mbox}}"}"""), ("verb", verb), ("activity", activity)))));
        Assert.Empty(Ids(await client.QueryAsync(Query(("verb", verb), ("registration", Guid.NewGuid().ToString())))));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Related_agents_also_matches_context_agents_teams_and_substatements(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var mbox = NewMbox();
        var agent = new JsonObject { ["mbox"] = mbox };
        JsonObject WithContext(JsonObject context)
        {
            var statement = Statement();
            statement["context"] = context;
            return statement;
        }

        var instructor = WithContext(new JsonObject { ["instructor"] = agent.DeepClone() });
        var team = WithContext(new JsonObject { ["team"] = new JsonObject { ["objectType"] = "Group", ["member"] = new JsonArray(agent.DeepClone()) } });
        var contextAgent = WithContext(new JsonObject { ["contextAgents"] = new JsonArray(new JsonObject { ["objectType"] = "contextAgent", ["agent"] = agent.DeepClone() }) });
        var contextGroup = WithContext(new JsonObject
        {
            ["contextGroups"] = new JsonArray(new JsonObject
            {
                ["objectType"] = "contextGroup",
                ["group"] = new JsonObject { ["objectType"] = "Group", ["member"] = new JsonArray(agent.DeepClone()) },
            }),
        });
        var subStatement = Statement();
        subStatement["object"] = new JsonObject
        {
            ["objectType"] = "SubStatement",
            ["actor"] = agent.DeepClone(),
            ["verb"] = new JsonObject { ["id"] = Attempted },
            ["object"] = new JsonObject { ["id"] = NewIri() },
        };
        var actor = Statement(mbox: mbox);

        var ids = await client.PutAllAsync(instructor, team, contextAgent, contextGroup, subStatement, actor);

        var filter = ("agent", agent.ToJsonString());
        Assert.Equal([ids[5]], Ids(await client.QueryAsync(Query(filter))));
        Assert.Equal(Enumerable.Reverse(ids), Ids(await client.QueryAsync(Query(filter, ("related_agents", "true")))));
        Assert.Equal([ids[5]], Ids(await client.QueryAsync(Query(filter, ("related_agents", "false")))));
    }

    [Fact]
    public async Task Related_agents_matches_the_authority()
    {
        var app = await hosts.CreateAsync(TestDatabase.Sqlite);
        var ids = await app.Client(Credentials.Writer).PutAllAsync(Statement(), Statement());
        await app.Client().PutAllAsync(Statement());
        var authority = new JsonObject { ["account"] = new JsonObject { ["homePage"] = AuthorityHomePage, ["name"] = Credentials.Writer } }.ToJsonString();

        var client = app.Client();
        Assert.Empty(Ids(await client.QueryAsync(Query(("agent", authority)))));
        Assert.Equal([ids[1], ids[0]], Ids(await client.QueryAsync(Query(("agent", authority), ("related_agents", "true")))));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Related_activities_also_matches_context_activities_and_substatements(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var activity = NewIri();
        JsonObject InContext(string kind)
        {
            var statement = Statement();
            statement["context"] = new JsonObject { ["contextActivities"] = new JsonObject { [kind] = new JsonArray(new JsonObject { ["id"] = activity }) } };
            return statement;
        }

        JsonObject InSubStatement(bool asObject)
        {
            var sub = new JsonObject
            {
                ["objectType"] = "SubStatement",
                ["actor"] = new JsonObject { ["mbox"] = NewMbox() },
                ["verb"] = new JsonObject { ["id"] = Attempted },
                ["object"] = new JsonObject { ["id"] = asObject ? activity : NewIri() },
            };
            if (!asObject)
            {
                sub["context"] = new JsonObject { ["contextActivities"] = new JsonObject { ["grouping"] = new JsonArray(new JsonObject { ["id"] = activity }) } };
            }

            var statement = Statement();
            statement["object"] = sub;
            return statement;
        }

        var ids = await client.PutAllAsync(
            InContext("parent"), InContext("grouping"), InContext("category"), InContext("other"),
            InSubStatement(asObject: true), InSubStatement(asObject: false), Statement(activity: activity));

        Assert.Equal([ids[6]], Ids(await client.QueryAsync(Query(("activity", activity)))));
        Assert.Equal(Enumerable.Reverse(ids), Ids(await client.QueryAsync(Query(("activity", activity), ("related_activities", "true")))));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Voided_statements_are_left_out_but_statements_targeting_them_match_their_filters(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var verb = NewIri("verbs");
        var activity = NewIri();
        var target = Guid.NewGuid();
        var voiding = Guid.NewGuid();
        var reference = Guid.NewGuid();
        var chained = Guid.NewGuid();
        var inContext = Guid.NewGuid();

        await PutAsync(client, target, Statement(activity: activity, verb: verb));
        await PutAsync(client, voiding, Voiding(target));
        await PutAsync(client, reference, Referencing(target));
        await PutAsync(client, chained, Referencing(reference));
        var contextOnly = Statement();
        contextOnly["context"] = new JsonObject { ["statement"] = new JsonObject { ["objectType"] = "StatementRef", ["id"] = target.ToString() } };
        await PutAsync(client, inContext, contextOnly);

        // IEEE 9274.1.1, 4.1.6.1: targeting statements match through any number of StatementRefs; a StatementRef
        // in context does not count; the voided target itself is never returned
        Assert.Equal([chained, reference, voiding], Ids(await client.QueryAsync(Query(("verb", verb)))));
        Assert.Equal([chained, reference, voiding], Ids(await client.QueryAsync(Query(("activity", activity)))));

        // Time bounds apply to the targeting statement itself
        var referenceStored = (await client.GetStoredAsync(reference))["stored"]!.GetValue<string>();
        Assert.Equal([chained], Ids(await client.QueryAsync(Query(("verb", verb), ("since", referenceStored)))));
        Assert.Equal([reference, voiding], Ids(await client.QueryAsync(Query(("verb", verb), ("until", referenceStored)))));

        using var voided = await client.GetStatementAsync(target, "voidedStatementId");
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Since_is_exclusive_and_until_inclusive_on_the_stored_time(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var verb = NewIri("verbs");
        var ids = new List<Guid>();
        var stored = new List<DateTimeOffset>();
        for (var i = 0; i < 3; i++)
        {
            ids.AddRange(await client.PutAllAsync(Statement(verb: verb)));
            stored.Add(XapiTimestamp.Parse((await client.GetStoredAsync(ids[i]))["stored"]!.GetValue<string>(), XapiVersion.V2_0_0));

            // A new millisecond, so each statement has its own stored time
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        string At(DateTimeOffset value) => XapiTimestamp.Format(value);

        Assert.Equal([ids[2], ids[1]], Ids(await client.QueryAsync(Query(("verb", verb), ("since", At(stored[0]))))));
        Assert.Equal([ids[1], ids[0]], Ids(await client.QueryAsync(Query(("verb", verb), ("until", At(stored[1]))))));
        Assert.Equal([ids[1]], Ids(await client.QueryAsync(Query(("verb", verb), ("since", At(stored[0])), ("until", At(stored[1]))))));

        // Sub-millisecond bounds: stored times have whole milliseconds
        Assert.Equal([ids[2], ids[1]], Ids(await client.QueryAsync(Query(("verb", verb), ("since", At(stored[0].AddTicks(5000)))))));
        Assert.Equal([ids[0]], Ids(await client.QueryAsync(Query(("verb", verb), ("until", At(stored[1].AddTicks(-1)))))));

        // Other offsets are converted to UTC
        Assert.Equal([ids[2], ids[1]], Ids(await client.QueryAsync(Query(("verb", verb), ("since", stored[0].ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", System.Globalization.CultureInfo.InvariantCulture))))));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task More_pages_stay_stable_when_statements_are_stored_between_pages(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var verb = NewIri("verbs");
        var ids = await client.PutAllAsync([.. Enumerable.Range(0, 5).Select(_ => Statement(verb: verb))]);

        var page1 = await client.QueryAsync(Query(("verb", verb), ("limit", "2")));
        Assert.Equal([ids[4], ids[3]], Ids(page1));

        var later = await client.PutAllAsync(Statement(verb: verb));
        var page2 = await client.MoreAsync(page1);
        Assert.Equal([ids[2], ids[1]], Ids(page2));

        await client.PutAllAsync(Statement(verb: verb));
        var page3 = await client.MoreAsync(page2);
        Assert.Equal([ids[0]], Ids(page3));
        Assert.Equal("", More(page3));

        // A new query sees the statements stored in between, newest first
        Assert.Equal(later[0], Ids(await client.QueryAsync(Query(("verb", verb)))).Skip(1).First());
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Ascending_pages_return_the_oldest_first_and_continue_with_new_statements(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var verb = NewIri("verbs");
        var ids = await client.PutAllAsync([.. Enumerable.Range(0, 4).Select(_ => Statement(verb: verb))]);

        var page = await client.QueryAsync(Query(("verb", verb), ("ascending", "true"), ("limit", "2")));
        Assert.Equal([ids[0], ids[1]], Ids(page));

        ids.AddRange(await client.PutAllAsync(Statement(verb: verb)));
        var seen = Ids(page);
        while (More(page).Length > 0)
        {
            page = await client.MoreAsync(page);
            seen.AddRange(Ids(page));
        }

        Assert.Equal(ids, seen);
    }

    [Fact]
    public async Task Limit_is_capped_at_the_server_maximum_and_zero_means_the_maximum()
    {
        var app = await hosts.CreateAsync(TestDatabase.Sqlite, services => services.AddXapiLrs(o => o.MaxStatementsPerPage = 3));
        var client = app.Client();
        var verb = NewIri("verbs");
        var ids = await client.PutAllAsync([.. Enumerable.Range(0, 5).Select(_ => Statement(verb: verb))]);

        Assert.Equal(3, Ids(await client.QueryAsync(Query(("verb", verb)))).Count);
        Assert.Equal(3, Ids(await client.QueryAsync(Query(("verb", verb), ("limit", "0")))).Count);
        Assert.Equal(3, Ids(await client.QueryAsync(Query(("verb", verb), ("limit", "4")))).Count);
        Assert.Equal(3, Ids(await client.QueryAsync(Query(("verb", verb), ("limit", "99999999999999999999")))).Count);
        Assert.Equal([ids[4]], Ids(await client.QueryAsync(Query(("verb", verb), ("limit", "1")))));

        // The page size travels in "more"
        var first = await client.QueryAsync(Query(("verb", verb), ("limit", "2")));
        Assert.Equal([ids[2], ids[1]], Ids(await client.MoreAsync(first)));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Ids_format_keeps_only_identifiers(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var verb = NewIri("verbs");
        var id = (await client.PutAllAsync(Described(verb, NewIri(), "Quiz")))[0];

        var statement = Single(await client.QueryAsync(Query(("verb", verb), ("format", "ids"))), id);

        Assert.Null(statement["actor"]!["name"]);
        Assert.Equal(verb, Assert.Single(statement["verb"]!.AsObject()).Value!.GetValue<string>());
        Assert.Equal("id", Assert.Single(statement["object"]!.AsObject()).Key);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Canonical_format_uses_the_lrs_definition_and_one_language_per_map(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var verb = NewIri("verbs");
        var activity = NewIri();
        var first = (await app.Client().PutAllAsync(Described(verb, activity, "Quiz")))[0];

        // A later statement adds a French name and a description to the LRS's definition of the activity
        var later = Statement(activity: activity);
        later["object"]!["definition"] = new JsonObject
        {
            ["name"] = new JsonObject { ["fr"] = "Examen" },
            ["description"] = new JsonObject { ["en-US"] = "A quiz", ["fr"] = "Un examen" },
        };
        await app.Client().PutAllAsync(later);

        var client = app.Client();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr-CA, de;q=0.5");
        var statement = Single(await client.QueryAsync(Query(("verb", verb), ("format", "canonical"))), first);

        // fr-CA has no exact entry, so its prefix fr is used; the verb display has no French, so de is next
        Assert.Equal("""{"de":"versucht"}""", statement["verb"]!["display"]!.ToJsonString());
        Assert.Equal("""{"fr":"Examen"}""", statement["object"]!["definition"]!["name"]!.ToJsonString());
        Assert.Equal("""{"fr":"Un examen"}""", statement["object"]!["definition"]!["description"]!.ToJsonString());
        Assert.Equal("Learner", statement["actor"]!["name"]!.GetValue<string>());

        // The single statement GET applies the same format
        using var single = await client.GetAsync($"/xapi/statements?statementId={first}&format=canonical", TestContext.Current.CancellationToken);
        Assert.Equal(statement.ToJsonString(), (await ReadObjectAsync(single)).ToJsonString());

        // exact returns the statement as stored, every language included
        var exact = Single(await client.QueryAsync(Query(("verb", verb))), first);
        Assert.Equal(2, exact["verb"]!["display"]!.AsObject().Count);
        Assert.Null(exact["object"]!["definition"]!["description"]);
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Query_responses_carry_consistent_through_and_last_modified(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var verb = NewIri("verbs");
        var ids = await client.PutAllAsync(Statement(verb: verb), Statement(verb: verb));
        var newest = XapiTimestamp.Parse((await client.GetStoredAsync(ids[1]))["stored"]!.GetValue<string>(), XapiVersion.V2_0_0);

        var before = DateTimeOffset.UtcNow;
        using var response = await client.GetStatementsAsync(Query(("verb", verb), ("ascending", "true")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TruncateToSeconds(newest), response.Content.Headers.LastModified);
        var consistentThrough = XapiTimestamp.Parse(Header(response, "X-Experience-API-Consistent-Through")!, XapiVersion.V2_0_0);
        Assert.InRange(consistentThrough, before.AddSeconds(-5), DateTimeOffset.UtcNow);

        using var empty = await client.GetStatementsAsync(Query(("verb", NewIri("verbs"))));
        Assert.Equal("""{"statements":[],"more":""}""", await empty.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Null(empty.Content.Headers.LastModified);
        Assert.NotNull(Header(empty, "X-Experience-API-Consistent-Through"));

        using var invalid = await client.GetStatementsAsync("limit=-1");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.NotNull(Header(invalid, "X-Experience-API-Consistent-Through"));
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Head_returns_the_headers_of_get_without_a_body(TestDatabase database)
    {
        var client = (await hosts.SharedAsync(database)).Client();
        var verb = NewIri("verbs");
        var id = (await client.PutAllAsync(Statement(verb: verb)))[0];

        foreach (var query in new[] { Query(("verb", verb)), $"statementId={id}" })
        {
            using var get = await client.GetStatementsAsync(query);
            using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/xapi/statements?" + query), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
            Assert.Empty(await head.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
            Assert.Equal(get.Content.Headers.ContentLength, head.Content.Headers.ContentLength);
            Assert.Equal(get.Content.Headers.LastModified, head.Content.Headers.LastModified);
            Assert.NotNull(Header(head, "X-Experience-API-Consistent-Through"));
        }
    }

    [Theory]
    [MemberData(nameof(TestDatabases.All), MemberType = typeof(TestDatabases))]
    public async Task Read_mine_queries_only_see_statements_stored_with_the_same_credential(TestDatabase database)
    {
        var app = await hosts.SharedAsync(database);
        var verb = NewIri("verbs");
        var mine = await app.Client(Credentials.Mine).PutAllAsync(Statement(verb: verb));
        var others = await app.Client(Credentials.Writer).PutAllAsync(Statement(verb: verb));

        Assert.Equal(mine, Ids(await app.Client(Credentials.Mine).QueryAsync(Query(("verb", verb)))));
        Assert.Equal([others[0], mine[0]], Ids(await app.Client(Credentials.Reader).QueryAsync(Query(("verb", verb)))));
    }

    [Fact]
    public async Task Attachments_true_answers_with_a_multipart_statement_result()
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client();
        var verb = NewIri("verbs");
        var id = (await client.PutAllAsync(Statement(verb: verb)))[0];

        using var response = await client.GetStatementsAsync(Query(("verb", verb), ("attachments", "true")));

        Assert.Equal("multipart/mixed", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = body[body.IndexOf('{', StringComparison.Ordinal)..(body.LastIndexOf('}') + 1)];
        Assert.Equal([id], Ids(JsonNode.Parse(json)!.AsObject()));
    }

    [Fact]
    public async Task Invalid_parameters_are_rejected_with_400()
    {
        var client = (await hosts.SharedAsync(TestDatabase.Sqlite)).Client();
        var id = Guid.NewGuid();
        var page = await client.QueryAsync("limit=1");
        var more = More(page)[(More(page).IndexOf('?', StringComparison.Ordinal) + 1)..];
        string[] invalid =
        [
            Query(("agent", "not json")),
            Query(("agent", """{"name":"No identifier"}""")),
            Query(("agent", """{"objectType":"Group","member":[{"mbox":"mailto:a@example.com"}]}""")),
            Query(("agent", """{"mbox":"a@example.com"}""")),
            Query(("verb", "not-an-iri")),
            Query(("activity", "")),
            Query(("registration", "123")),
            Query(("since", "yesterday")),
            Query(("until", "2026-13-01T00:00:00Z")),
            Query(("since", "2026-10-01T00:00:00")),
            "limit=-1", "limit=1.5", "limit=ten", "limit=",
            "format=EXACT", "format=full",
            "ascending=yes", "ascending=True", "related_agents=1", "related_activities=", "attachments=maybe",
            "Verb=http%3A%2F%2Fexample.com%2Fv", "unknown=1", "verb=http%3A%2F%2Fa.example&verb=http%3A%2F%2Fb.example",
            $"statementId={id}&voidedStatementId={id}",
            $"statementId={id}&limit=1", $"voidedStatementId={id}&ascending=true", $"statementId={id}&since=2020-01-01T00:00:00Z",
            $"statementId={id}&format=full", $"statementId={id}&attachments=1",
            "more=not-a-token", "more=", more + "&limit=1", "after=" + id,
        ];

        foreach (var query in invalid)
        {
            using var response = await client.GetStatementsAsync(query);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{query}: {(int)response.StatusCode}");
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    private static JsonObject Described(string verb, string activity, string name)
    {
        var statement = Statement(activity: activity, verb: verb);
        statement["actor"]!["name"] = "Learner";
        statement["verb"]!["display"] = new JsonObject { ["en-US"] = "attempted", ["de"] = "versucht" };
        statement["object"]!["definition"] = new JsonObject { ["name"] = new JsonObject { ["en-US"] = name, ["de"] = "Test" } };
        return statement;
    }

    private static JsonObject Referencing(Guid target)
    {
        var statement = Statement();
        statement["object"] = new JsonObject { ["objectType"] = "StatementRef", ["id"] = target.ToString() };
        return statement;
    }

    private static async Task PutAsync(HttpClient client, Guid id, JsonObject statement)
    {
        using var response = await client.PutStatementAsync(id, statement);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // A new millisecond, so time bounds can tell the statements apart
        await Task.Delay(5, TestContext.Current.CancellationToken);
    }

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Offset);
}
