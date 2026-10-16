using System.Diagnostics.CodeAnalysis;
using Xapi.Internal;

namespace Xapi;

/// <summary>
/// An absolute Internationalized Resource Identifier (RFC 3987), such as an Activity id, a Verb id or an
/// extension key. Also used for IRLs (<c>moreInfo</c>, <c>homePage</c>, <c>openid</c>, <c>fileUrl</c>).
/// </summary>
/// <remarks>
/// The original text is kept exactly and equality is ordinal string comparison (RFC 3987, 5.3.1), which is
/// what IEEE 9274.1.1, 4.2.7 allows an LRS to use. Unlike <see cref="Uri"/>, nothing is normalized.
/// </remarks>
public readonly record struct Iri
{
    private readonly string? _value;

    /// <summary>Creates an IRI.</summary>
    /// <param name="value">An absolute IRI: a scheme, a colon and a non-empty remainder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="value"/> is not an absolute IRI.</exception>
    public Iri(string value)
    {
        Guard.NotNull(value);
        if (!IsValid(value))
        {
            throw new FormatException($"'{value}' is not an absolute IRI.");
        }

        _value = value;
    }

    /// <summary>The IRI text exactly as given. Empty only for the default struct value.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Whether this is the default struct value, which is not a valid IRI.</summary>
    public bool IsEmpty => _value is null;

    /// <summary>Tries to create an IRI.</summary>
    /// <param name="value">The candidate text.</param>
    /// <param name="iri">The IRI when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is an absolute IRI.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out Iri iri)
    {
        if (value is not null && IsValid(value))
        {
            iri = new Iri(value, validated: true);
            return true;
        }

        iri = default;
        return false;
    }

    /// <summary>Creates an IRI from an absolute <see cref="Uri"/>, using the text the URI was created from.</summary>
    /// <param name="uri">An absolute URI.</param>
    /// <returns>The IRI.</returns>
    /// <exception cref="FormatException">The URI is relative or not a valid IRI.</exception>
    public static Iri FromUri(Uri uri)
    {
        Guard.NotNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            throw new FormatException($"'{uri.OriginalString}' is not an absolute URI.");
        }

        return new Iri(uri.OriginalString);
    }

    /// <summary>Converts to a <see cref="Uri"/>. Some valid IRIs may not be accepted by <see cref="Uri"/>.</summary>
    /// <returns>The URI.</returns>
    public Uri ToUri() => new(Value, UriKind.Absolute);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The IRI text.</returns>
    public override string ToString() => Value;

    private Iri(string value, bool validated)
    {
        _ = validated;
        _value = value;
    }

    /// <summary>Checks absolute-IRI syntax (RFC 3987, 2.2) without resolving or normalizing anything.</summary>
    internal static bool IsValid(string value)
    {
        if (value.Length < 3 || !XapiFormats.IsAsciiLetter(value[0]))
        {
            return false;
        }

        var colon = -1;
        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (c == ':')
            {
                colon = i;
                break;
            }

            if (!(XapiFormats.IsAsciiLetter(c) || XapiFormats.IsDigit(c) || c is '+' or '-' or '.'))
            {
                return false;
            }
        }

        if (colon < 0 || colon == value.Length - 1)
        {
            return false;
        }

        var start = colon + 1;
        var authorityEnd = start;
        if (value.Length > start + 1 && value[start] == '/' && value[start + 1] == '/')
        {
            authorityEnd = start + 2;
            while (authorityEnd < value.Length && value[authorityEnd] is not ('/' or '?' or '#'))
            {
                authorityEnd++;
            }
        }

        var seenHash = false;
        for (var i = start; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '%':
                    if (i + 2 >= value.Length || !XapiFormats.IsHexChar(value[i + 1]) || !XapiFormats.IsHexChar(value[i + 2]))
                    {
                        return false;
                    }

                    break;
                case '#':
                    if (seenHash)
                    {
                        return false;
                    }

                    seenHash = true;
                    break;
                case '[' or ']':
                    if (i >= authorityEnd)
                    {
                        return false;
                    }

                    break;
                case <= ' ' or '\u007f' or '<' or '>' or '"' or '{' or '}' or '|' or '\\' or '^' or '`':
                    return false;
                case >= '\u0080' and <= '\u009f':
                    return false;
                case '￾' or '￿':
                    return false;
                default:
                    if (char.IsHighSurrogate(c))
                    {
                        if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                        {
                            return false;
                        }

                        i++;
                    }
                    else if (char.IsLowSurrogate(c))
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }
}
