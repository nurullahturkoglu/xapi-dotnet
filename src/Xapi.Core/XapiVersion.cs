namespace Xapi;

/// <summary>
/// An xAPI version profile. Reading, writing and validation take one explicitly, so one process can serve
/// both versions.
/// </summary>
/// <remarks>
/// Only two profiles exist: xAPI 1.0.3 and xAPI 2.0.0 (IEEE 9274.1.1). Patch versions map onto these
/// profiles, see <see cref="TryParseHeader(string?, out XapiVersion)"/>.
/// </remarks>
public readonly struct XapiVersion : IEquatable<XapiVersion>
{
    private const int Version103 = 1;
    private const int Version200 = 2;

    private readonly int _value;

    private XapiVersion(int value) => _value = value;

    /// <summary>xAPI 1.0.3.</summary>
    public static XapiVersion V1_0_3 { get; } = new(Version103);

    /// <summary>xAPI 2.0.0, published as IEEE 9274.1.1-2023.</summary>
    public static XapiVersion V2_0_0 { get; } = new(Version200);

    /// <summary>
    /// The value for the <c>X-Experience-API-Version</c> header and the statement <c>version</c> property:
    /// <c>1.0.3</c> or <c>2.0.0</c>. The default struct value reports <c>2.0.0</c>.
    /// </summary>
    public string HeaderValue => _value == Version103 ? "1.0.3" : "2.0.0";

    /// <summary>Whether this is the xAPI 2.0.0 profile (also true for the default struct value).</summary>
    public bool IsV2 => _value != Version103;

    /// <summary>Equality operator.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when both are the same profile.</returns>
    public static bool operator ==(XapiVersion left, XapiVersion right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when the profiles differ.</returns>
    public static bool operator !=(XapiVersion left, XapiVersion right) => !left.Equals(right);

    /// <summary>
    /// Maps an <c>X-Experience-API-Version</c> header value onto a profile: <c>1.0</c> and <c>1.0.x</c> map to
    /// 1.0.3, <c>2.0</c> and <c>2.0.x</c> map to 2.0.0. Anything else (including 0.9x and 2.1.0 or later) fails.
    /// </summary>
    /// <remarks>IEEE 9274.1.1, 4.1.7: "2.0" is accepted as 2.0.0 and versions 2.1.0 or greater are rejected.</remarks>
    /// <param name="value">The header value.</param>
    /// <param name="version">The matching profile when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the value names a supported version.</returns>
    public static bool TryParseHeader(string? value, out XapiVersion version)
    {
        version = default;
        if (value is null)
        {
            return false;
        }

        var text = value.Trim();
        if (MatchesMinor(text, "1.0"))
        {
            version = V1_0_3;
            return true;
        }

        if (MatchesMinor(text, "2.0"))
        {
            version = V2_0_0;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool Equals(XapiVersion other) => IsV2 == other.IsV2;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is XapiVersion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => IsV2 ? Version200 : Version103;

    /// <summary>Returns <see cref="HeaderValue"/>.</summary>
    /// <returns>The version string.</returns>
    public override string ToString() => HeaderValue;

    private static bool MatchesMinor(string text, string minor)
    {
        if (text == minor)
        {
            return true;
        }

        if (text.Length <= minor.Length + 1 || !text.StartsWith(minor + ".", StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = minor.Length + 1; i < text.Length; i++)
        {
            if (!Internal.XapiFormats.IsDigit(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}
