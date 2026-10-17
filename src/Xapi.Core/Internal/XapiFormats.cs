namespace Xapi.Internal;

/// <summary>Leaf format checks shared by the value types, the JSON reader and the validator.</summary>
internal static class XapiFormats
{
    private static readonly HashSet<string> GrandfatheredTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "en-GB-oed", "i-ami", "i-bnn", "i-default", "i-enochian", "i-hak", "i-klingon", "i-lux", "i-mingo",
        "i-navajo", "i-pwn", "i-tao", "i-tay", "i-tsu", "sgn-BE-FR", "sgn-BE-NL", "sgn-CH-DE",
        "art-lojban", "cel-gaulish", "no-bok", "no-nyn", "zh-guoyu", "zh-hakka", "zh-min", "zh-min-nan", "zh-xiang",
    };

    /// <summary>Checks that <paramref name="tag"/> is a well-formed RFC 5646 language tag (section 2.1 ABNF).</summary>
    public static bool IsLanguageTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return false;
        }

        if (IsLanguageAndRegion(tag!) || GrandfatheredTags.Contains(tag!))
        {
            return true;
        }

        var parts = tag!.Split('-');
        foreach (var part in parts)
        {
            if (part.Length is 0 or > 8 || !IsAlphanumeric(part))
            {
                return false;
            }
        }

        var n = parts.Length;
        var i = 0;
        if (IsSingleton(parts[0], 'x'))
        {
            return IsPrivateUseTail(parts, 1);
        }

        var language = parts[0];
        if (language.Length < 2 || !IsAlpha(language))
        {
            return false;
        }

        i++;
        if (language.Length <= 3)
        {
            var extlangs = 0;
            while (i < n && extlangs < 3 && parts[i].Length == 3 && IsAlpha(parts[i]))
            {
                i++;
                extlangs++;
            }
        }

        if (i < n && parts[i].Length == 4 && IsAlpha(parts[i]))
        {
            i++;
        }

        if (i < n && ((parts[i].Length == 2 && IsAlpha(parts[i])) || (parts[i].Length == 3 && IsDigits(parts[i]))))
        {
            i++;
        }

        while (i < n && IsVariant(parts[i]))
        {
            i++;
        }

        while (i < n && parts[i].Length == 1 && !IsSingleton(parts[i], 'x'))
        {
            i++;
            var start = i;
            while (i < n && parts[i].Length >= 2)
            {
                i++;
            }

            if (i == start)
            {
                return false;
            }
        }

        if (i < n && IsSingleton(parts[i], 'x'))
        {
            return IsPrivateUseTail(parts, i + 1);
        }

        return i == n;
    }

    // The most common shapes, en, fil, en-US or es-419, accepted without splitting the tag. Anything else goes
    // through the full grammar.
    private static bool IsLanguageAndRegion(string tag)
    {
        var language = 0;
        while (language < tag.Length && IsAsciiLetter(tag[language]))
        {
            language++;
        }

        if (language is < 2 or > 3)
        {
            return false;
        }

        var region = tag.Length - language - 1;
        if (region < 0)
        {
            return true;
        }

        if (tag[language] != '-')
        {
            return false;
        }

        return region switch
        {
            2 => IsAsciiLetter(tag[language + 1]) && IsAsciiLetter(tag[language + 2]),
            3 => IsDigit(tag[language + 1]) && IsDigit(tag[language + 2]) && IsDigit(tag[language + 3]),
            _ => false,
        };
    }

    /// <summary>Checks for a <c>mailto:</c> IRI with a plausible addr-spec (no query, no whitespace).</summary>
    public static bool IsMbox(string? value)
    {
        const string Scheme = "mailto:";
        if (value is null || !value.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return false;
        }

        var at = value.LastIndexOf('@');
        if (at <= Scheme.Length || at == value.Length - 1)
        {
            return false;
        }

        for (var i = Scheme.Length; i < at; i++)
        {
            if (!IsLocalPartChar(value[i]))
            {
                return false;
            }
        }

        // Dot-separated labels, none of them empty
        var labelLength = 0;
        for (var i = at + 1; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '.')
            {
                if (labelLength == 0)
                {
                    return false;
                }

                labelLength = 0;
            }
            else if (IsAsciiLetterOrDigit(c) || c == '-' || c > 0x7F)
            {
                labelLength++;
            }
            else
            {
                return false;
            }
        }

        return labelLength > 0;
    }

    public static bool IsSha1Hex(string? value) => value is { Length: 40 } && IsHex(value);

    /// <summary>Hex SHA-224, SHA-256, SHA-384 or SHA-512 digest.</summary>
    public static bool IsSha2Hex(string? value) => value is { Length: 56 or 64 or 96 or 128 } && IsHex(value);

    /// <summary>The standard 8-4-4-4-12 hexadecimal UUID form, nothing else (no braces, no missing hyphens).</summary>
    public static bool IsUuid(string? value)
    {
        if (value is not { Length: 36 })
        {
            return false;
        }

        for (var i = 0; i < 36; i++)
        {
            var c = value[i];
            var ok = i is 8 or 13 or 18 or 23 ? c == '-' : IsHexChar(c);
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Statement <c>version</c> values: 1.0.3 accepts <c>1.0</c> and <c>1.0.x</c> (Semantic Versioning, optional
    /// pre-release and build); 2.0.0 additionally accepts exactly <c>2.0.0</c>.
    /// </summary>
    public static bool IsStatementVersion(string? value, XapiVersion version)
    {
        if (value is null)
        {
            return false;
        }

        if (version == XapiVersion.V2_0_0 && value == "2.0.0")
        {
            return true;
        }

        if (value == "1.0")
        {
            return true;
        }

        if (!value.StartsWith("1.0.", StringComparison.Ordinal))
        {
            return false;
        }

        var i = 4;
        var digits = 0;
        while (i < value.Length && IsDigit(value[i]))
        {
            i++;
            digits++;
        }

        if (digits == 0)
        {
            return false;
        }

        if (i < value.Length && value[i] == '-')
        {
            i = SkipDotSeparatedIdentifiers(value, i + 1);
            if (i < 0)
            {
                return false;
            }
        }

        if (i < value.Length && value[i] == '+')
        {
            i = SkipDotSeparatedIdentifiers(value, i + 1);
            if (i < 0)
            {
                return false;
            }
        }

        return i == value.Length;
    }

    /// <summary>An Internet media type: <c>type/subtype</c> tokens with optional parameters.</summary>
    public static bool IsMediaType(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var semicolon = value!.IndexOf(';');
        var essence = semicolon < 0 ? value : value.Substring(0, semicolon);
        var slash = essence.IndexOf('/');
        if (slash <= 0 || slash == essence.Length - 1)
        {
            return false;
        }

        for (var i = 0; i < essence.Length; i++)
        {
            if (i != slash && !IsTokenChar(essence[i]))
            {
                return false;
            }
        }

        if (semicolon >= 0)
        {
            for (var i = semicolon; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool IsDigit(char c) => c is >= '0' and <= '9';

    public static bool IsHexChar(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    public static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsAsciiLetterOrDigit(char c) => IsAsciiLetter(c) || IsDigit(c);

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!IsHexChar(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAlpha(string value)
    {
        foreach (var c in value)
        {
            if (!IsAsciiLetter(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDigits(string value)
    {
        foreach (var c in value)
        {
            if (!IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAlphanumeric(string value)
    {
        foreach (var c in value)
        {
            if (!IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSingleton(string part, char singleton) =>
        part.Length == 1 && char.ToLowerInvariant(part[0]) == singleton;

    private static bool IsVariant(string part) =>
        part.Length is >= 5 and <= 8 || (part.Length == 4 && IsDigit(part[0]));

    private static bool IsPrivateUseTail(string[] parts, int start) => start < parts.Length;

    private static bool IsLocalPartChar(char c) =>
        IsAsciiLetterOrDigit(c) || c > 0x7F || "!#$%&'*+/=?^_`{|}~.-".AsSpan().IndexOf(c) >= 0;

    private static bool IsTokenChar(char c) => IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".AsSpan().IndexOf(c) >= 0;

    private static int SkipDotSeparatedIdentifiers(string value, int i)
    {
        var start = i;
        while (i < value.Length && (IsAsciiLetterOrDigit(value[i]) || value[i] == '-' || value[i] == '.'))
        {
            if (value[i] == '.' && (i == start || value[i - 1] == '.'))
            {
                return -1;
            }

            i++;
        }

        return i == start || value[i - 1] == '.' ? -1 : i;
    }
}
