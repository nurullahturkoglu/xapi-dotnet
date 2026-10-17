using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>
/// The hash, media type and length of stored attachment data, as listed by
/// <see cref="IStatementStore.GetAttachmentSummariesAsync"/>; the data itself is read with
/// <see cref="IStatementStore.GetAttachmentContentAsync"/>.
/// </summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public sealed class AttachmentSummary
{
    /// <summary>Creates a summary.</summary>
    /// <param name="sha2">The SHA-2 of the data as hexadecimal text; stored in lower case.</param>
    /// <param name="contentType">The media type of the data.</param>
    /// <param name="length">The length of the data in bytes.</param>
    public AttachmentSummary(string sha2, string contentType, long length)
    {
        ArgumentException.ThrowIfNullOrEmpty(sha2);
        ArgumentException.ThrowIfNullOrEmpty(contentType);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Sha2 = sha2.ToLowerInvariant();
        ContentType = contentType;
        Length = length;
    }

    /// <summary>The SHA-2 of the data as lower-case hexadecimal text.</summary>
    public string Sha2 { get; }

    /// <summary>The media type of the data.</summary>
    public string ContentType { get; }

    /// <summary>The length of the data in bytes.</summary>
    public long Length { get; }
}
