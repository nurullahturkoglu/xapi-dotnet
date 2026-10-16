using System.Net;
using System.Text.Json.Nodes;
using Xapi.Lrs.Storage;
using Xapi.Lrs.Tests.Infrastructure;
using static Xapi.Lrs.Tests.Infrastructure.StatementApi;

namespace Xapi.Lrs.Tests;

// IEEE 9274.1.1, 4.1: every resource that accepts GET accepts HEAD and answers it like the GET, without a body.
public class HeadRequestTests(LrsHosts hosts)
{
    private static readonly string[] ComparedHeaders =
    [
        "Content-Type", "Content-Length", "ETag", "Last-Modified", "X-Experience-API-Version",
    ];

    [Theory]
    [InlineData("1.0.3")]
    [InlineData("2.0.0")]
    public async Task HEAD_answers_every_GET_resource_like_GET_without_the_body(string version)
    {
        // A fresh LRS: on the shared one, other tests change the statement list between the GET and the HEAD
        var app = await hosts.CreateAsync(TestDatabase.Sqlite);
        var client = app.Client(version: version);
        var id = Guid.NewGuid();
        var mbox = NewMbox();
        var statement = Statement(id, mbox: mbox);
        statement["actor"]!["name"] = "Head";
        statement["object"]!["definition"] = new JsonObject { ["name"] = new JsonObject { ["en"] = "Head" } };
        using (var put = await client.PutStatementAsync(id, statement))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        var documents = Enum.GetValues<DocumentResource>().Select(r => DocTarget.New(r)).ToList();
        foreach (var target in documents)
        {
            await client.CreateAsync(target);
        }

        var agent = Uri.EscapeDataString(new JsonObject { ["mbox"] = mbox }.ToJsonString());
        var activity = Uri.EscapeDataString(statement["object"]!["id"]!.GetValue<string>());
        List<string> urls =
        [
            "/xapi/about",
            "/xapi/statements",
            $"/xapi/statements?statementId={id}",
            $"/xapi/statements?statementId={Guid.NewGuid()}",
            $"/xapi/agents?agent={agent}",
            $"/xapi/activities?activityId={activity}",
            "/xapi/activities",
            .. documents.SelectMany(d => new[] { d.Url, d.ListUrl() }),
        ];

        foreach (var url in urls)
        {
            using var get = await client.SendAsync(HttpMethod.Get, url);
            using var head = await client.SendAsync(HttpMethod.Head, url);
            var getBody = await get.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            var headBody = await head.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

            Assert.True(get.StatusCode == head.StatusCode, $"{url}: GET {get.StatusCode}, HEAD {head.StatusCode}");
            Assert.True(getBody.Length > 0, url);
            Assert.Empty(headBody);
            foreach (var name in ComparedHeaders)
            {
                Assert.True(Header(get, name) == Header(head, name), $"{url}: {name} differs between GET and HEAD");
            }

            Assert.Equal(getBody.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), Header(head, "Content-Length"));
        }
    }
}
