using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>A statement read back from an <see cref="IStatementStore"/>.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage)]
public sealed class StoredStatement
{
    /// <summary>Creates a stored statement.</summary>
    /// <param name="id">The statement id.</param>
    /// <param name="stored">The time the LRS stored it.</param>
    /// <param name="version">The statement's <c>version</c> property.</param>
    /// <param name="isVoided">Whether another stored statement voids it.</param>
    /// <param name="authorityKey">The identifier of its authority, see <see cref="StatementRecord.AuthorityKey"/>.</param>
    /// <param name="json">The statement in the <c>exact</c> format.</param>
    public StoredStatement(Guid id, DateTimeOffset stored, string version, bool isVoided, string? authorityKey, string json)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrEmpty(json);
        Id = id;
        Stored = stored;
        Version = version;
        IsVoided = isVoided;
        AuthorityKey = authorityKey;
        Json = json;
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

    /// <summary>The statement in the <c>exact</c> format.</summary>
    public string Json { get; }
}
