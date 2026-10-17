using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>
/// Stores State, Activity Profile and Agent Profile documents. The default implementation uses
/// <see cref="XapiLrsDbContext"/>.
/// </summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public interface IDocumentStore
{
    /// <summary>Reads one document.</summary>
    /// <param name="scope">The scope of the document.</param>
    /// <param name="id">The document id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The document, or <see langword="null"/> when there is none.</returns>
    Task<StoredDocument?> GetAsync(DocumentScope scope, string id, CancellationToken cancellationToken = default);

    /// <summary>Lists the documents of a scope, each id once.</summary>
    /// <param name="scope">The scope. For state, a scope without a registration covers every registration.</param>
    /// <param name="since">When set, only documents changed after this time (exclusive).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The documents, in no particular order.</returns>
    Task<IReadOnlyList<DocumentSummary>> ListAsync(DocumentScope scope, DateTimeOffset? since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a document and applies the change <paramref name="decide"/> returns for it, atomically: no other
    /// change to the document can happen between the read and the write. This is what makes ETag
    /// preconditions safe under concurrent requests (IEEE 9274.1.1, 4.1.4).
    /// </summary>
    /// <param name="scope">The scope of the document.</param>
    /// <param name="id">The document id.</param>
    /// <param name="decide">
    /// Called with the current document, or <see langword="null"/> when there is none. It may be called more
    /// than once when the store retries after a concurrent write, so it must not have other side effects than
    /// recording its last decision.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The document after the change, or <see langword="null"/> when there is none.</returns>
    Task<StoredDocument?> ChangeAsync(DocumentScope scope, string id, Func<StoredDocument?, DocumentChange> decide, CancellationToken cancellationToken = default);

    /// <summary>Deletes every document of a scope.</summary>
    /// <param name="scope">The scope. For state, a scope without a registration covers every registration.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the documents are deleted.</returns>
    Task DeleteAllAsync(DocumentScope scope, CancellationToken cancellationToken = default);
}
