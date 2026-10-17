using System.Text.Json;
using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>
/// A document of the State, Activity Profile or Agent Profile resource: arbitrary bytes with a content type, plus
/// the ETag and Last-Modified time the LRS returned with it.
/// </summary>
/// <remarks>
/// <para>Equality compares the content type, the bytes, the ETag and the Last-Modified time.</para>
/// <para>
/// The document keeps the memory it is given; the bytes are not copied. Do not change them while the document is
/// in use, for example between creating it and sending it. Copy the buffer first if it is reused.
/// </para>
/// </remarks>
/// <param name="Content">The document bytes. Kept without a copy, so they must not change afterwards.</param>
/// <param name="ContentType">The media type, for example <c>application/json</c>.</param>
public sealed record XapiDocument(ReadOnlyMemory<byte> Content, string ContentType)
{
    private const string JsonMediaType = "application/json";

    private readonly string _contentType = Guard.NotEmpty(ContentType);

    /// <summary>The media type, for example <c>application/json</c>.</summary>
    public string ContentType
    {
        get => _contentType;
        init => _contentType = Guard.NotEmpty(value);
    }

    /// <summary>
    /// The <c>ETag</c> response header, exactly as the LRS sent it. Pass it to
    /// <see cref="DocumentPrecondition.IfMatch"/> to update or delete only this version of the document.
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>The <c>Last-Modified</c> response header.</summary>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>Creates an <c>application/json</c> document.</summary>
    /// <param name="json">The JSON value. The State and profile resources merge only JSON objects on POST.</param>
    /// <returns>The document.</returns>
    public static XapiDocument FromJson(JsonElement json) => new(JsonBytes.From(json), JsonMediaType);

    /// <summary>Parses the content as JSON.</summary>
    /// <returns>The parsed value, which stays valid after this document is gone.</returns>
    /// <exception cref="JsonException">The content is not JSON.</exception>
    public JsonElement ToJson()
    {
        using var document = JsonDocument.Parse(Content);
        return document.RootElement.Clone();
    }

    /// <inheritdoc/>
    public bool Equals(XapiDocument? other) =>
        other is not null
        && string.Equals(ContentType, other.ContentType, StringComparison.Ordinal)
        && string.Equals(ETag, other.ETag, StringComparison.Ordinal)
        && LastModified == other.LastModified
        && Content.Span.SequenceEqual(other.Content.Span);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ContentType) ^ Content.Length;
}
