using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>
/// The raw data of an Attachment, received as a part of a <c>multipart/mixed</c> statement request and stored
/// once per SHA-2 however many statements refer to it (IEEE 9274.1.1, 4.1.3).
/// </summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public sealed class AttachmentContent
{
    /// <summary>Creates attachment content.</summary>
    /// <param name="sha2">The SHA-2 of <paramref name="content"/> as hexadecimal text; stored in lower case.</param>
    /// <param name="contentType">The media type of the data.</param>
    /// <param name="content">The raw bytes.</param>
    public AttachmentContent(string sha2, string contentType, ReadOnlyMemory<byte> content)
    {
        ArgumentException.ThrowIfNullOrEmpty(sha2);
        ArgumentException.ThrowIfNullOrEmpty(contentType);
        Sha2 = sha2.ToLowerInvariant();
        ContentType = contentType;
        Content = content;
    }

    /// <summary>The SHA-2 of <see cref="Content"/> as lower-case hexadecimal text.</summary>
    public string Sha2 { get; }

    /// <summary>The media type of the data.</summary>
    public string ContentType { get; }

    /// <summary>The raw bytes.</summary>
    public ReadOnlyMemory<byte> Content { get; }
}
