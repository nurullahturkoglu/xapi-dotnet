using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>
/// The raw data of an Attachment, sent or received as a part of a <c>multipart/mixed</c> request or response and
/// matched to its Attachment object by SHA-2 (IEEE 9274.1.1, 4.1.3).
/// </summary>
/// <remarks>Equality compares the hash (ignoring case), the content type and the bytes.</remarks>
/// <param name="Sha2">The SHA-256 of <paramref name="Content"/> as hexadecimal text.</param>
/// <param name="ContentType">The media type, sent as the part's <c>Content-Type</c>.</param>
/// <param name="Content">The raw bytes.</param>
public sealed record AttachmentPayload(string Sha2, string ContentType, ReadOnlyMemory<byte> Content)
{
    private readonly string _sha2 = Guard.NotEmpty(Sha2);
    private readonly string _contentType = Guard.NotEmpty(ContentType);

    /// <summary>The SHA-256 of <see cref="Content"/> as hexadecimal text.</summary>
    public string Sha2
    {
        get => _sha2;
        init => _sha2 = Guard.NotEmpty(value);
    }

    /// <summary>The media type, sent as the part's <c>Content-Type</c>.</summary>
    public string ContentType
    {
        get => _contentType;
        init => _contentType = Guard.NotEmpty(value);
    }

    /// <summary>Creates a payload and computes its SHA-2.</summary>
    /// <param name="content">The raw bytes.</param>
    /// <param name="contentType">The media type, for example <c>image/png</c>.</param>
    /// <returns>The payload.</returns>
    public static AttachmentPayload Create(ReadOnlyMemory<byte> content, string contentType) =>
        new(Sha256Hex.Compute(content.Span), contentType, content);

    /// <summary>
    /// Creates the Attachment object that describes this payload, with <c>contentType</c>, <c>length</c> and
    /// <c>sha2</c> taken from it, for the <see cref="Statement.Attachments"/> of the Statement it is sent with.
    /// </summary>
    /// <param name="usageType">What the Attachment is for, for example
    /// <c>http://adlnet.gov/expapi/attachments/signature</c>.</param>
    /// <param name="display">The display name.</param>
    /// <returns>The Attachment.</returns>
    public Attachment ToAttachment(Iri usageType, LanguageMap display) =>
        new(usageType, display, ContentType, Content.Length, Sha2);

    /// <inheritdoc/>
    public bool Equals(AttachmentPayload? other) =>
        other is not null
        && string.Equals(Sha2, other.Sha2, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ContentType, other.ContentType, StringComparison.Ordinal)
        && Content.Span.SequenceEqual(other.Content.Span);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Sha2);
}
