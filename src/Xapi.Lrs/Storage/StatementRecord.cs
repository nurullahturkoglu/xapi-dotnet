using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>
/// A statement ready to be stored: every property the LRS sets (<c>id</c>, <c>stored</c>, <c>timestamp</c>,
/// <c>authority</c>, <c>version</c>) is filled in, and its storage key is assigned.
/// </summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public sealed class StatementRecord
{
    /// <summary>Creates a record.</summary>
    /// <param name="key">The storage key, see <see cref="Key"/>.</param>
    /// <param name="statement">The prepared statement. Its <see cref="Statement.Id"/> and <see cref="Statement.Stored"/> must be set.</param>
    /// <param name="utf8Json">The statement as returned to clients in the <c>exact</c> format, as UTF-8 JSON. Kept without a copy, so the bytes must not change afterwards.</param>
    /// <param name="authorityKey">An identifier of the authority, see <see cref="AuthorityKey"/>.</param>
    /// <exception cref="ArgumentException">The statement has no id or stored time, or <paramref name="utf8Json"/> is empty.</exception>
    public StatementRecord(Guid key, Statement statement, ReadOnlyMemory<byte> utf8Json, string authorityKey)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentException.ThrowIfNullOrEmpty(authorityKey);
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("The statement JSON must not be empty.", nameof(utf8Json));
        }

        if (statement.Id is null || statement.Stored is null)
        {
            throw new ArgumentException("The statement must have an id and a stored time.", nameof(statement));
        }

        Key = key;
        Statement = statement;
        Utf8Json = utf8Json;
        AuthorityKey = authorityKey;
    }

    /// <summary>
    /// A monotonic UUIDv7 whose time part is the statement's <c>stored</c> time. It orders statements by
    /// stored time, and the default store's continuation tokens mark a position in this order (ADR-0020).
    /// </summary>
    public Guid Key { get; }

    /// <summary>The prepared statement.</summary>
    public Statement Statement { get; }

    /// <summary>The statement id.</summary>
    public Guid Id => Statement.Id!.Value;

    /// <summary>
    /// The statement as returned to clients in the <c>exact</c> format, as UTF-8 JSON. A store keeps these bytes
    /// and returns them in <see cref="StoredStatement.Utf8Json"/>.
    /// </summary>
    public ReadOnlyMemory<byte> Utf8Json { get; }

    /// <summary>
    /// An identifier of the statement's authority, used to find "my" statements for the
    /// <c>statements/read/mine</c> scope.
    /// </summary>
    public string AuthorityKey { get; }

    /// <summary>
    /// The attachment data received with the statement, for the Attachment objects that have it. Data shared by
    /// several statements of a batch is the same instance in each record and is stored once.
    /// </summary>
    public IReadOnlyList<AttachmentContent> Attachments
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = [];
}
