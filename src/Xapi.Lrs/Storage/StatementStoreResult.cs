using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>The outcome of <see cref="IStatementStore.StoreAsync"/>.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage)]
public sealed class StatementStoreResult
{
    private StatementStoreResult(Guid? conflictingStatementId) => ConflictingStatementId = conflictingStatementId;

    /// <summary>The batch was stored, or every statement in it was already stored with an equivalent statement.</summary>
    public static StatementStoreResult Stored { get; } = new(null);

    /// <summary>
    /// The id of a statement in the batch that is already stored with a statement that is not equivalent,
    /// or <see langword="null"/> when the batch was stored. Nothing was written in that case.
    /// </summary>
    public Guid? ConflictingStatementId { get; }

    /// <summary>Whether the batch was rejected because of <see cref="ConflictingStatementId"/>.</summary>
    [MemberNotNullWhen(true, nameof(ConflictingStatementId))]
    public bool IsConflict => ConflictingStatementId is not null;

    /// <summary>Creates a conflict result.</summary>
    /// <param name="statementId">The id that is already stored with a different statement.</param>
    /// <returns>The result.</returns>
    public static StatementStoreResult Conflict(Guid statementId) => new(statementId);
}
