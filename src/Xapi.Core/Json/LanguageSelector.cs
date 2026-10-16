namespace Xapi.Json;

/// <summary>Picks the one entry of a language map that canonical format returns (IEEE 9274.1.1, 4.1.6.1).</summary>
internal static class LanguageSelector
{
    /// <remarks>
    /// Ranges are tried in order: an exact tag match, then a tag that starts with the range (RFC 4647 basic
    /// filtering, so <c>en</c> matches <c>en-US</c>), then the range with trailing subtags removed (RFC 4647
    /// lookup, so <c>en-US</c> matches <c>en</c>). <c>*</c> matches the first entry. Without a match the
    /// <c>und</c> entry is used, then the first entry.
    /// </remarks>
    public static KeyValuePair<string, string>? Select(LanguageMap map, IReadOnlyList<string>? ranges)
    {
        if (map.Count == 0)
        {
            return null;
        }

        foreach (var raw in ranges ?? [])
        {
            var semicolon = raw.IndexOf(';');
            var range = (semicolon < 0 ? raw : raw.Substring(0, semicolon)).Trim();
            if (range.Length == 0)
            {
                continue;
            }

            if (range == "*")
            {
                return map.First();
            }

            foreach (var entry in map)
            {
                if (string.Equals(entry.Key, range, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            foreach (var entry in map)
            {
                if (entry.Key.StartsWith(range + "-", StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            var truncated = range;
            int dash;
            while ((dash = truncated.LastIndexOf('-')) > 0)
            {
                truncated = truncated.Substring(0, dash);
                if (map.TryGetValue(truncated, out var value))
                {
                    return new KeyValuePair<string, string>(map.Keys.First(k => string.Equals(k, truncated, StringComparison.OrdinalIgnoreCase)), value);
                }
            }
        }

        foreach (var entry in map)
        {
            if (string.Equals(entry.Key, "und", StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return map.First();
    }
}
