using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>
/// The raw data of an Attachment, sent or received as a part of a <c>multipart/mixed</c> request or response and
/// matched to its Attachment object by SHA-2 (IEEE 9274.1.1, 4.1.3).
/// </summary>
/// <remarks>
/// <para>Equality compares the hash, the content type and the bytes.</para>
/// <para>
/// The payload keeps the memory it is given; the bytes are not copied. Do not change them afterwards: the payload
/// would then send data that no longer matches its <see cref="Sha2"/>, and an LRS that checks the hash would reject
/// it. Copy the buffer first if it is reused.
/// </para>
/// </remarks>
public sealed record AttachmentPayload
{
    private readonly string _sha2;
    private readonly string _contentType;

    /// <summary>Creates a payload.</summary>
    /// <param name="sha2">The SHA-256 of <paramref name="content"/> as hexadecimal text, in either case.</param>
    /// <param name="contentType">The media type, sent as the part's <c>Content-Type</c>.</param>
    /// <param name="content">The raw bytes. Kept without a copy, so they must not change afterwards.</param>
    public AttachmentPayload(string sha2, string contentType, ReadOnlyMemory<byte> content)
    {
        _sha2 = Guard.NotEmpty(sha2).ToLowerInvariant();
        _contentType = Guard.NotEmpty(contentType);
        Content = content;
    }

    /// <summary>The SHA-256 of <see cref="Content"/> as lowercase hexadecimal text.</summary>
    /// <remarks>A hash given in uppercase is stored in lowercase, so equal payloads compare equal whatever its case.</remarks>
    public string Sha2
    {
        get => _sha2;
        init => _sha2 = Guard.NotEmpty(value).ToLowerInvariant();
    }

    /// <summary>The media type, sent as the part's <c>Content-Type</c>.</summary>
    public string ContentType
    {
        get => _contentType;
        init => _contentType = Guard.NotEmpty(value);
    }

    /// <summary>The raw bytes. They are not copied, so they must not change while the payload is in use.</summary>
    public ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>Creates a payload and computes its SHA-2.</summary>
    /// <param name="content">The raw bytes. Kept without a copy, so they must not change afterwards.</param>
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
        && string.Equals(Sha2, other.Sha2, StringComparison.Ordinal)
        && string.Equals(ContentType, other.ContentType, StringComparison.Ordinal)
        && Content.Span.SequenceEqual(other.Content.Span);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Sha2);
}
