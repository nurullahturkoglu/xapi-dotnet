using Xapi.Json;
using Xapi.Lrs.Security;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Statements;

// Fills in what the LRS sets on every statement it stores (IEEE 9274.1.1, 4.2.4.2) and hands the batch to the
// store.
internal sealed class StatementWriter(IStatementStore store, StatementWriteTracker tracker)
{
    private readonly UuidV7Generator _keys = UuidV7Generator.Shared;

    // attachments: per statement, the attachment data received with it; null when none was
    public async Task<(StatementStoreResult Result, List<Guid> Ids)> WriteAsync(
        IReadOnlyList<Statement> statements,
        IReadOnlyList<IReadOnlyList<AttachmentContent>>? attachments,
        XapiVersion version,
        XapiCaller caller,
        CancellationToken cancellationToken)
    {
        using var inFlight = tracker.Begin();
        var records = new List<StatementRecord>(statements.Count);
        for (var i = 0; i < statements.Count; i++)
        {
            records.Add(Prepare(statements[i], attachments?[i] ?? [], version, caller));
        }

        var result = await store.StoreAsync(records, Scopes.CanDefine(caller.Scopes), cancellationToken).ConfigureAwait(false);
        return (result, records.ConvertAll(r => r.Id));
    }

    private StatementRecord Prepare(Statement statement, IReadOnlyList<AttachmentContent> attachments, XapiVersion version, XapiCaller caller)
    {
        // ADR-020: the key's time part is the stored time, so ordering by key is ordering by stored
        var key = _keys.NewGuid();
        var stored = UuidV7Generator.GetTimestamp(key);
        var prepared = statement with
        {
            Id = statement.Id ?? _keys.NewGuid(),
            Stored = stored,
            Timestamp = statement.Timestamp ?? stored,
            // The authority is always overwritten from the credentials (4.2.4.2 "should overwrite")
            Authority = caller.Authority,
            // 4.2.4.3: statements lacking a version get 2.0.0; xAPI 1.0.3 (Data 2.4.10) says 1.0.0
            Version = statement.Version ?? (version.IsV2 ? "2.0.0" : "1.0.0"),
        };

        var json = XapiJson.SerializeStatement(prepared, version);
        return new StatementRecord(key, prepared, json, caller.AuthorityKey) { Attachments = attachments };
    }
}
