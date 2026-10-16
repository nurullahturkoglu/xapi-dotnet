using Xapi.Internal;

namespace Xapi;

/// <summary>The header of a Statement attachment: what it is, its type, size and hash.</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.6 "Attachments Table".</remarks>
public sealed record Attachment
{
    private readonly Iri _usageType;
    private readonly LanguageMap _display;
    private readonly string _contentType;
    private readonly string _sha2;

    /// <summary>Creates an attachment header.</summary>
    /// <param name="usageType">What the attachment is used for, for example <c>http://adlnet.gov/expapi/attachments/signature</c>.</param>
    /// <param name="display">The title.</param>
    /// <param name="contentType">The Internet media type of the data.</param>
    /// <param name="length">The length of the data in octets.</param>
    /// <param name="sha2">The hexadecimal SHA-2 hash of the data.</param>
    public Attachment(Iri usageType, LanguageMap display, string contentType, long length, string sha2)
    {
        _usageType = Guard.NotDefault(usageType);
        _display = Guard.NotNull(display);
        _contentType = Guard.NotNull(contentType);
        Length = length;
        _sha2 = Guard.NotNull(sha2);
    }

    /// <summary>What the attachment is used for.</summary>
    public Iri UsageType
    {
        get => _usageType;
        init => _usageType = Guard.NotDefault(value);
    }

    /// <summary>The title.</summary>
    public LanguageMap Display
    {
        get => _display;
        init => _display = Guard.NotNull(value);
    }

    /// <summary>A description.</summary>
    public LanguageMap? Description { get; init; }

    /// <summary>The Internet media type of the data.</summary>
    public string ContentType
    {
        get => _contentType;
        init => _contentType = Guard.NotNull(value);
    }

    /// <summary>The length of the data in octets.</summary>
    public long Length { get; init; }

    /// <summary>The hexadecimal SHA-2 hash of the data. Required even when <see cref="FileUrl"/> is set.</summary>
    public string Sha2
    {
        get => _sha2;
        init => _sha2 = Guard.NotNull(value);
    }

    /// <summary>An IRL the data can be retrieved from.</summary>
    public Iri? FileUrl { get; init; }
}
