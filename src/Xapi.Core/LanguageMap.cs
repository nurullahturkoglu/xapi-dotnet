using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Xapi.Internal;

namespace Xapi;

/// <summary>
/// An immutable xAPI language map: RFC 5646 language tags mapped to strings in that language.
/// </summary>
/// <remarks>
/// Keys are validated as well-formed RFC 5646 tags (IEEE 9274.1.1, 4.2.7 "Language Maps") and compared
/// case-insensitively, because language tags are case-insensitive. The original key spelling and insertion
/// order are kept for writing. Equality ignores order.
/// </remarks>
public sealed class LanguageMap : IReadOnlyDictionary<string, string>, IEquatable<LanguageMap>
{
    private readonly KeyValuePair<string, string>[] _entries;
    private readonly Dictionary<string, string> _lookup;

    /// <summary>Creates a language map.</summary>
    /// <param name="entries">The entries.</param>
    /// <exception cref="ArgumentException">A key is not a well-formed language tag, a value is null, or two keys differ only in case.</exception>
    public LanguageMap(IEnumerable<KeyValuePair<string, string>> entries)
    {
        Guard.NotNull(entries);
        var list = new List<KeyValuePair<string, string>>();
        _lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!XapiFormats.IsLanguageTag(entry.Key))
            {
                throw new ArgumentException($"'{entry.Key}' is not a well-formed RFC 5646 language tag.", nameof(entries));
            }

            if (entry.Value is null)
            {
                throw new ArgumentException($"The value for '{entry.Key}' is null.", nameof(entries));
            }

            if (_lookup.ContainsKey(entry.Key))
            {
                throw new ArgumentException($"The language tag '{entry.Key}' appears more than once.", nameof(entries));
            }

            _lookup.Add(entry.Key, entry.Value);
            list.Add(entry);
        }

        _entries = list.ToArray();
    }

    private LanguageMap(KeyValuePair<string, string>[] entries, Dictionary<string, string> lookup)
    {
        _entries = entries;
        _lookup = lookup;
    }

    /// <summary>
    /// Creates a map from the first <paramref name="count"/> entries without checking them again: the caller has
    /// already checked that the tags are well-formed and distinct and that no value is null. Takes ownership of
    /// <paramref name="entries"/>.
    /// </summary>
    internal static LanguageMap FromChecked(KeyValuePair<string, string>[] entries, int count)
    {
        if (count < entries.Length)
        {
            Array.Resize(ref entries, count);
        }

        var lookup = new Dictionary<string, string>(count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            lookup.Add(entry.Key, entry.Value);
        }

        return new LanguageMap(entries, lookup);
    }

    /// <summary>The empty language map.</summary>
    public static LanguageMap Empty { get; } = new([], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public int Count => _entries.Length;

    /// <summary>The language tags, in insertion order.</summary>
    public IEnumerable<string> Keys => _entries.Select(e => e.Key);

    /// <summary>The strings, in insertion order.</summary>
    public IEnumerable<string> Values => _entries.Select(e => e.Value);

    /// <summary>Gets the string for a language tag (case-insensitive).</summary>
    /// <param name="key">The language tag.</param>
    /// <returns>The string.</returns>
    /// <exception cref="KeyNotFoundException">The tag is not in the map.</exception>
    public string this[string key] => _lookup[key];

    /// <summary>Equality operator.</summary>
    /// <param name="left">The first map.</param>
    /// <param name="right">The second map.</param>
    /// <returns><see langword="true"/> when both maps hold the same entries.</returns>
    public static bool operator ==(LanguageMap? left, LanguageMap? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">The first map.</param>
    /// <param name="right">The second map.</param>
    /// <returns><see langword="true"/> when the maps differ.</returns>
    public static bool operator !=(LanguageMap? left, LanguageMap? right) => !(left == right);

    /// <summary>Creates a map with a single entry.</summary>
    /// <param name="languageTag">An RFC 5646 language tag, for example <c>en-US</c>.</param>
    /// <param name="value">The string.</param>
    /// <returns>The map.</returns>
    public static LanguageMap Create(string languageTag, string value) =>
        new([new KeyValuePair<string, string>(languageTag, value)]);

    /// <summary>Checks whether a string is a well-formed RFC 5646 language tag.</summary>
    /// <param name="languageTag">The candidate tag.</param>
    /// <returns><see langword="true"/> when the tag is well-formed.</returns>
    public static bool IsValidLanguageTag([NotNullWhen(true)] string? languageTag) => XapiFormats.IsLanguageTag(languageTag);

    /// <summary>Returns a new map with the entry added, or replaced when the tag already exists.</summary>
    /// <param name="languageTag">An RFC 5646 language tag.</param>
    /// <param name="value">The string.</param>
    /// <returns>The new map.</returns>
    public LanguageMap With(string languageTag, string value)
    {
        var entries = _entries.Where(e => !string.Equals(e.Key, languageTag, StringComparison.OrdinalIgnoreCase));
        return new LanguageMap(entries.Append(new KeyValuePair<string, string>(languageTag, value)));
    }

    /// <inheritdoc/>
    public bool ContainsKey(string key) => _lookup.ContainsKey(key);

    /// <inheritdoc/>
#if NET
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) => _lookup.TryGetValue(key, out value);
#else
    public bool TryGetValue(string key, out string value) => _lookup.TryGetValue(key, out value!);
#endif

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, string>>)_entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public bool Equals(LanguageMap? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_entries.Length != other._entries.Length)
        {
            return false;
        }

        foreach (var entry in _entries)
        {
            if (!other._lookup.TryGetValue(entry.Key, out var value) || !string.Equals(entry.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LanguageMap other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = _entries.Length;
        foreach (var entry in _entries)
        {
            hash ^= Hash.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(entry.Key), StringComparer.Ordinal.GetHashCode(entry.Value));
        }

        return hash;
    }

    /// <summary>Returns the entries formatted as <c>{en-US: text}</c>.</summary>
    /// <returns>A diagnostic string.</returns>
    public override string ToString() => "{" + string.Join(", ", _entries.Select(e => e.Key + ": " + e.Value)) + "}";
}
