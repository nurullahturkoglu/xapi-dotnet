using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>A statement read back from an <see cref="IStatementStore"/>.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public sealed class StoredStatement
{
    /// <summary>Creates a stored statement.</summary>
    /// <param name="id">The statement id.</param>
    /// <param name="stored">The time the LRS stored it.</param>
    /// <param name="version">The statement's <c>version</c> property.</param>
    /// <param name="isVoided">Whether another stored statement voids it.</param>
    /// <param name="authorityKey">The identifier of its authority, see <see cref="StatementRecord.AuthorityKey"/>.</param>
    /// <param name="utf8Json">The statement in the <c>exact</c> format, as UTF-8 JSON. Kept without a copy, so the bytes must not change afterwards.</param>
    /// <exception cref="ArgumentException"><paramref name="utf8Json"/> is empty.</exception>
    public StoredStatement(Guid id, DateTimeOffset stored, string version, bool isVoided, string? authorityKey, ReadOnlyMemory<byte> utf8Json)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("The statement JSON must not be empty.", nameof(utf8Json));
        }

        Id = id;
        Stored = stored;
        Version = version;
        IsVoided = isVoided;
        AuthorityKey = authorityKey;
        Utf8Json = utf8Json;
    }

    /// <summary>The statement id.</summary>
    public Guid Id { get; }

    /// <summary>The time the LRS stored the statement.</summary>
    public DateTimeOffset Stored { get; }

    /// <summary>The statement's <c>version</c> property, such as <c>1.0.0</c> or <c>2.0.0</c>.</summary>
    public string Version { get; }

    /// <summary>Whether another stored statement voids this one.</summary>
    public bool IsVoided { get; }

    /// <summary>The identifier of the statement's authority.</summary>
    public string? AuthorityKey { get; }

    /// <summary>
    /// The statement in the <c>exact</c> format, as UTF-8 JSON: the bytes the store received in
    /// <see cref="StatementRecord.Utf8Json"/>. The LRS writes them into responses unchanged and without
    /// validating them again.
    /// </summary>
    public ReadOnlyMemory<byte> Utf8Json { get; }
}
