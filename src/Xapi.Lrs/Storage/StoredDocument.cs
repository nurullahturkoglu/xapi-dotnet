using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>A document read from an <see cref="IDocumentStore"/>.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage)]
public sealed class StoredDocument
{
    /// <summary>Creates a stored document.</summary>
    /// <param name="id">The document id (<c>stateId</c> or <c>profileId</c>).</param>
    /// <param name="content">The content, exactly as stored.</param>
    /// <param name="contentType">The content type it was stored with.</param>
    /// <param name="etag">The entity tag of the content, without quotes.</param>
    /// <param name="updated">When the document was last changed.</param>
    public StoredDocument(string id, ReadOnlyMemory<byte> content, string contentType, string etag, DateTimeOffset updated)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrEmpty(contentType);
        ArgumentException.ThrowIfNullOrEmpty(etag);
        Id = id;
        Content = content;
        ContentType = contentType;
        ETag = etag;
        Updated = updated;
    }

    /// <summary>The document id (<c>stateId</c> or <c>profileId</c>).</summary>
    public string Id { get; }

    /// <summary>The content, exactly as stored.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>The content type the document was stored with.</summary>
    public string ContentType { get; }

    /// <summary>The entity tag of the content, without quotes. It changes whenever the content changes.</summary>
    public string ETag { get; }

    /// <summary>When the document was last changed.</summary>
    public DateTimeOffset Updated { get; }
}
