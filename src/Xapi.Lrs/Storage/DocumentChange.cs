using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>What <see cref="IDocumentStore.ChangeAsync"/> does with a document: keep, store or delete it.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public sealed class DocumentChange
{
    private DocumentChange(DocumentChangeKind kind, ReadOnlyMemory<byte> content, string? contentType)
    {
        Kind = kind;
        Content = content;
        ContentType = contentType;
    }

    /// <summary>Leaves the document as it is, for example because a precondition failed.</summary>
    public static DocumentChange None { get; } = new(DocumentChangeKind.None, ReadOnlyMemory<byte>.Empty, null);

    /// <summary>Deletes the document if it exists.</summary>
    public static DocumentChange Delete { get; } = new(DocumentChangeKind.Delete, ReadOnlyMemory<byte>.Empty, null);

    /// <summary>The kind of change.</summary>
    public DocumentChangeKind Kind { get; }

    /// <summary>The new content, for <see cref="DocumentChangeKind.Store"/>.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>The new content type, for <see cref="DocumentChangeKind.Store"/>.</summary>
    public string? ContentType { get; }

    /// <summary>Creates or replaces the document.</summary>
    /// <param name="content">The new content.</param>
    /// <param name="contentType">The new content type.</param>
    /// <returns>The change.</returns>
    public static DocumentChange Store(ReadOnlyMemory<byte> content, string contentType)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentType);
        return new(DocumentChangeKind.Store, content, contentType);
    }
}
