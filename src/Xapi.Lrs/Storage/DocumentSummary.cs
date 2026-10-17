using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>The id and modification time of a document, as listed by <see cref="IDocumentStore.ListAsync"/>.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public sealed class DocumentSummary
{
    /// <summary>Creates a summary.</summary>
    /// <param name="id">The document id.</param>
    /// <param name="updated">When the document was last changed.</param>
    public DocumentSummary(string id, DateTimeOffset updated)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
        Updated = updated;
    }

    /// <summary>The document id (<c>stateId</c> or <c>profileId</c>).</summary>
    public string Id { get; }

    /// <summary>When the document was last changed.</summary>
    public DateTimeOffset Updated { get; }
}
