using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Xapi.Lrs.Storage;
using Xapi.Lrs.Tests.Infrastructure;
using Xapi.Testing;

namespace Xapi.Lrs.Tests;

public class TelemetryTests(LrsHosts hosts)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Requests_are_measured_with_resource_method_status_and_version()
    {
        var app = await hosts.CreateAsync(TestDatabase.Sqlite);
        using var metrics = Record(app);
        using var client = app.Client();
        using var v1 = app.Client(version: "1.0.3");
        using var noVersion = app.Client(version: null);
        var id = Guid.NewGuid();

        using (var response = await client.PutStatementAsync(id, StatementApi.Statement(id)))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using (var response = await client.GetAsync("/xapi/statements?color=red", Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var response = await v1.GetAsync("/xapi/about", Ct))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var response = await noVersion.GetAsync("/xapi/activities/state", Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var response = await client.GetAsync("/xapi/nothing-here", Ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var durations = metrics.Of(XapiLrsDiagnostics.RequestDuration);
        Assert.All(durations, d => Assert.Equal("s", d.Unit));
        Assert.All(durations, d => Assert.InRange(d.Value, double.Epsilon, 60));
        Assert.Equal(
            [
                "http.request.method=GET, http.response.status_code=200, xapi.resource=about, xapi.version=1.0.3",
                // No version: the request was refused before one was negotiated
                "http.request.method=GET, http.response.status_code=400, xapi.resource=activities/state",
                "http.request.method=GET, http.response.status_code=400, xapi.resource=statements, xapi.version=2.0.0",
                "http.request.method=GET, http.response.status_code=404, xapi.resource=_OTHER, xapi.version=2.0.0",
                "http.request.method=PUT, http.response.status_code=204, xapi.resource=statements, xapi.version=2.0.0",
            ],
            durations.Select(d => d.TagText).Order(StringComparer.Ordinal));

        Assert.Equal(
            [
                "error.type=400, http.request.method=GET, http.response.status_code=400, xapi.resource=activities/state",
                "error.type=400, http.request.method=GET, http.response.status_code=400, xapi.resource=statements, xapi.version=2.0.0",
                "error.type=404, http.request.method=GET, http.response.status_code=404, xapi.resource=_OTHER, xapi.version=2.0.0",
            ],
            metrics.Of(XapiLrsDiagnostics.Errors).Select(e => e.TagText).Order(StringComparer.Ordinal));
        Assert.All(metrics.Of(XapiLrsDiagnostics.Errors), e => Assert.Equal(1, e.Value));
    }

    [Fact]
    public async Task An_unhandled_exception_is_recorded_as_a_server_error_with_its_type()
    {
        var app = await hosts.CreateAsync(TestDatabase.Sqlite, services => services.AddSingleton<IStatementStore, FailingStatementStore>());
        using var metrics = Record(app);
        using var client = app.Client();

        using var response = await client.PostStatementsAsync(new JsonArray(StatementApi.Statement()));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        const string Expected = "error.type=System.InvalidOperationException, http.request.method=POST, http.response.status_code=500, xapi.resource=statements, xapi.version=2.0.0";
        Assert.Equal(Expected, Assert.Single(metrics.Of(XapiLrsDiagnostics.RequestDuration)).TagText);
        Assert.Equal(Expected, Assert.Single(metrics.Of(XapiLrsDiagnostics.Errors)).TagText);
    }

    [Fact]
    public async Task Stored_statements_are_counted_per_xapi_version_and_conflicts_are_not()
    {
        var app = await hosts.CreateAsync(TestDatabase.Sqlite);
        using var metrics = Record(app);
        using var client = app.Client();
        using var v1 = app.Client(version: "1.0.3");
        var id = Guid.NewGuid();

        using (var response = await client.PostStatementsAsync(new JsonArray(StatementApi.Statement(), StatementApi.Statement())))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var response = await v1.PutStatementAsync(id, StatementApi.Statement(id)))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using (var response = await client.PutStatementAsync(id, StatementApi.Statement(id, activity: StatementApi.NewIri())))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        var stored = metrics.Of(XapiLrsDiagnostics.StatementsStored);
        Assert.All(stored, s => Assert.Equal("{statement}", s.Unit));
        Assert.Equal(
            new Dictionary<string, double> { ["xapi.version=1.0.3"] = 1, ["xapi.version=2.0.0"] = 2 },
            metrics.Sums(XapiLrsDiagnostics.StatementsStored));
    }

    [Fact]
    public async Task Statement_and_document_operations_are_traced_inside_the_request()
    {
        // The request carries a trace id of its own, so activities from tests running at the same time are left out
        var traceId = ActivityTraceId.CreateRandom();
        var stopped = new ConcurrentQueue<System.Diagnostics.Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is XapiLrsDiagnostics.ActivitySourceName or "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId && activity.Source.Name == XapiLrsDiagnostics.ActivitySourceName)
                {
                    stopped.Enqueue(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var app = await hosts.SharedAsync(TestDatabase.Sqlite);
        using var client = app.Client();
        client.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        var activity = StatementApi.NewIri();
        var doc = DocTarget.New(DocumentResource.State);

        using (var response = await client.PostStatementsAsync(new JsonArray(StatementApi.Statement(activity: activity), StatementApi.Statement(activity: activity))))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var response = await client.GetAsync($"/xapi/statements?activity={Uri.EscapeDataString(activity)}", Ct))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await client.CreateAsync(doc);
        using (var response = await client.PostDocAsync(doc, """{"b":2}"""))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using (var response = await client.DeleteDocAsync(doc))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(
            [
                "xapi.lrs.statements.store: xapi.statements.conflict=False, xapi.statements.count=2, xapi.version=2.0.0",
                "xapi.lrs.statements.query: xapi.statements.count=2, xapi.version=2.0.0",
                "xapi.lrs.documents.write: xapi.document.operation=replace, xapi.resource=activities/state",
                "xapi.lrs.documents.write: xapi.document.operation=merge, xapi.resource=activities/state",
                "xapi.lrs.documents.write: xapi.document.operation=delete, xapi.resource=activities/state",
            ],
            stopped.Select(a => $"{a.OperationName}: {string.Join(", ", a.TagObjects.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => $"{t.Key}={t.Value}"))}"));
        Assert.All(stopped, a => Assert.NotEqual(default, a.ParentSpanId));
        Assert.All(stopped, a => Assert.Equal(ActivityStatusCode.Unset, a.Status));
    }

    [Fact]
    public async Task No_tag_carries_agents_iris_ids_or_credential_keys()
    {
        var key = "probe-key-" + Guid.NewGuid().ToString("N");
        var app = await hosts.CreateAsync(TestDatabase.Sqlite, services => services.AddXapiLrs(o => o.Credentials.Add(key, LrsApp.Credentials.SecretOf(key), XapiScopes.All)));
        var activities = new ConcurrentQueue<System.Diagnostics.Activity>();
        var traceId = ActivityTraceId.CreateRandom();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == XapiLrsDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        using var metrics = Record(app);
        using var client = app.Client(key);
        client.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        var mbox = StatementApi.NewMbox();
        var activity = StatementApi.NewIri();
        var registration = Guid.NewGuid();
        var id = Guid.NewGuid();
        var statement = StatementApi.Statement(id, activity, mbox);
        statement["context"] = new JsonObject { ["registration"] = registration.ToString() };
        var doc = DocTarget.New(DocumentResource.State, registration) with { Agent = new JsonObject { ["mbox"] = mbox }.ToJsonString(), ActivityId = activity };
        var agent = Uri.EscapeDataString(new JsonObject { ["mbox"] = mbox }.ToJsonString());

        using (await client.PutStatementAsync(id, statement))
        using (await client.GetAsync($"/xapi/statements?agent={agent}&registration={registration}", Ct))
        using (await client.GetAsync($"/xapi/statements?agent=%7Bbroken&registration={registration}", Ct))
        using (await client.PutDocAsync(doc, """{"a":1}""", ifNoneMatch: "*"))
        using (await client.GetAsync($"/xapi/agents?agent={agent}", Ct))
        using (await client.GetAsync($"/xapi/{Uri.EscapeDataString(mbox)}", Ct))
        {
        }

        var values = metrics.All.SelectMany(m => m.Tags.Values)
            .Concat(activities.Where(a => a.TraceId == traceId).SelectMany(a => a.TagObjects.Select(t => t.Value)))
            .Select(v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "")
            .ToList();
        Assert.NotEmpty(metrics.Of(XapiLrsDiagnostics.RequestDuration));
        Assert.NotEmpty(activities);
        string[] secrets = [mbox["mailto:".Length..], activity, registration.ToString(), id.ToString(), key, doc.Id];
        Assert.All(values, value => Assert.DoesNotContain(secrets, s => value.Contains(s, StringComparison.OrdinalIgnoreCase)));
    }

    private static MetricRecorder Record(LrsApp app)
    {
        // Each app has its own IMeterFactory, so other tests' requests are not recorded
        var meterFactory = app.Services.GetRequiredService<IMeterFactory>();
        return new MetricRecorder(i => i.Meter.Name == XapiLrsDiagnostics.MeterName && ReferenceEquals(i.Meter.Scope, meterFactory));
    }

    private sealed class FailingStatementStore : IStatementStore
    {
        public Task<StatementStoreResult> StoreAsync(IReadOnlyList<StatementRecord> statements, bool updateDefinitions, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The store is broken.");

        public Task<StoredStatement?> GetAsync(Guid statementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StatementPage> QueryAsync(StatementFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Iri, Activity>> GetActivitiesAsync(IReadOnlyCollection<Iri> activityIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Person> GetPersonAsync(Agent agent, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AttachmentSummary>> GetAttachmentSummariesAsync(IReadOnlyCollection<Guid> statementIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AttachmentContent?> GetAttachmentContentAsync(string sha2, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
