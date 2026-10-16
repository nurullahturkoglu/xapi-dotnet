namespace Xapi.Client.IntegrationTests;

public class AttachmentTests(LrsqlFixture lrs)
{
    // Control bytes, CRLF, a dash run and multi-byte characters, all of which a careless multipart reader or writer
    // mangles. Only valid UTF-8 though: lrsql v0.9.9 returns attachment data decoded as UTF-8, so other bytes come
    // back as U+FFFD and the client (correctly) rejects them for not matching their sha2.
    private static readonly byte[] Binary = [0, 1, 2, 9, 13, 10, .. "--\r\n--xapi\r\nçğüş € 𝄞\n"u8.ToArray(), 0x7F];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Attachment_data_goes_to_the_lrs_and_comes_back_byte_for_byte(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var registration = Guid.NewGuid();
        var payload = AttachmentPayload.Create(Binary, "application/octet-stream");
        var statement = Data.NewStatement(registration: registration) with
        {
            Attachments = [payload.ToAttachment(new Iri("https://example.com/attachment-usage/evidence"), LanguageMap.Create("en-US", "Evidence"))],
        };

        var id = await client.SaveStatementAsync(statement, [payload], Ct);
        var result = await client.QueryStatementsAsync(new StatementQuery { Registration = registration, Attachments = true }, Ct);

        var stored = Assert.Single(result.Statements);
        Assert.Equal(id, stored.Id);
        Assert.Equal(statement.Attachments, stored.Attachments);
        var received = Assert.Single(result.Attachments);
        Assert.Equal(payload.Sha2, received.Sha2, ignoreCase: true);
        Assert.Equal(Binary, received.Content.ToArray());
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Batch_with_shared_attachment_sends_the_data_once(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var registration = Guid.NewGuid();
        var payload = AttachmentPayload.Create(Binary, "image/png");
        var attachment = payload.ToAttachment(new Iri("https://example.com/attachment-usage/evidence"), LanguageMap.Create("en-US", "Evidence"));
        Statement[] batch =
        [
            Data.NewStatement(registration: registration) with { Attachments = [attachment] },
            Data.NewStatement(registration: registration) with { Attachments = [attachment] },
        ];

        await client.SaveStatementsAsync(batch, [payload], Ct);
        var result = await client.QueryStatementsAsync(new StatementQuery { Registration = registration, Attachments = true }, Ct);

        Assert.Equal(2, result.Statements.Count);
        Assert.All(result.Attachments, a => Assert.Equal(Binary, a.Content.ToArray()));
        Assert.NotEmpty(result.Attachments);
    }

    [Theory]
    [MemberData(nameof(Data.Versions), MemberType = typeof(Data))]
    public async Task Query_without_attachments_returns_json_only(string version)
    {
        var client = lrs.CreateClient(Data.Parse(version));
        var registration = Guid.NewGuid();
        var payload = AttachmentPayload.Create(Binary, "application/octet-stream");
        var statement = Data.NewStatement(registration: registration) with
        {
            Attachments = [payload.ToAttachment(new Iri("https://example.com/attachment-usage/evidence"), LanguageMap.Create("en-US", "Evidence"))],
        };
        await client.SaveStatementAsync(statement, [payload], Ct);

        var result = await client.QueryStatementsAsync(new StatementQuery { Registration = registration }, Ct);

        Assert.Single(result.Statements);
        Assert.Empty(result.Attachments);
    }
}
