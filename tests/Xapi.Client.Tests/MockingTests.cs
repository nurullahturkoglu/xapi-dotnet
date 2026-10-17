using Xapi.Client.Tests.TestData;
using static Xapi.Client.Tests.Fakes.TestToken;

namespace Xapi.Client.Tests;

public class MockingTests
{
    [Fact]
    public async Task Subclass_made_with_the_protected_constructor_replaces_what_it_overrides()
    {
        var client = new RecordingClient();

        var id = await client.SaveStatementAsync(Samples.Statement(), cancellationToken: Ct);

        Assert.Equal(Samples.Id(1), id);
        Assert.Single(client.Saved);
    }

    [Fact]
    public async Task Methods_it_does_not_override_throw_InvalidOperationException()
    {
        var client = new RecordingClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetVersionAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAboutAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetStatementAsync(Samples.Id(1), cancellationToken: Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var statement in client.QueryAllStatementsAsync(new StatementQuery(), Ct))
            {
            }
        });

        // Overriding SaveStatementAsync does not change VoidStatementAsync
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.VoidStatementAsync(Samples.Id(1), Samples.Learner, Ct));
        Assert.Empty(client.Saved);
    }

    private sealed class RecordingClient : XapiClient
    {
        public List<Statement> Saved { get; } = [];

        public override Task<Guid> SaveStatementAsync(Statement statement, IReadOnlyList<AttachmentPayload>? attachments = null, CancellationToken cancellationToken = default)
        {
            Saved.Add(statement);
            return Task.FromResult(Samples.Id(Saved.Count));
        }
    }
}
