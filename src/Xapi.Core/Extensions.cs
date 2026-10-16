using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Xapi.Internal;

namespace Xapi;

/// <summary>
/// An immutable xAPI extensions map: IRI keys mapped to arbitrary JSON values, as found in
/// <c>result.extensions</c>, <c>context.extensions</c>, Activity definitions and the About resource.
/// </summary>
/// <remarks>
/// Values are kept as <see cref="JsonElement"/> clones, so they are written back exactly as read (numbers keep
/// their original text). Equality compares values deeply: object property order is ignored, arrays are ordered,
/// numbers compare by value. Values are never validated (IEEE 9274.1.1, 4.2.7 "Extensions").
/// </remarks>
public sealed class Extensions : IReadOnlyDictionary<Iri, JsonElement>, IEquatable<Extensions>
{
    private readonly KeyValuePair<Iri, JsonElement>[] _entries;
    private readonly Dictionary<Iri, JsonElement> _lookup;

    /// <summary>Creates an extensions map. Each value is cloned so it does not depend on its source document.</summary>
    /// <param name="entries">The entries.</param>
    /// <exception cref="ArgumentException">A key is the default IRI or appears more than once, or a value is the default <see cref="JsonElement"/>.</exception>
    public Extensions(IEnumerable<KeyValuePair<Iri, JsonElement>> entries)
    {
        Guard.NotNull(entries);
        var list = new List<KeyValuePair<Iri, JsonElement>>();
        _lookup = [];
        foreach (var entry in entries)
        {
            if (entry.Key.IsEmpty)
            {
                throw new ArgumentException("An extension key must not be the default IRI.", nameof(entries));
            }

            if (_lookup.ContainsKey(entry.Key))
            {
                throw new ArgumentException($"The extension key '{entry.Key}' appears more than once.", nameof(entries));
            }

            if (entry.Value.ValueKind == JsonValueKind.Undefined)
            {
                throw new ArgumentException($"The value for '{entry.Key}' is the default JsonElement.", nameof(entries));
            }

            var value = entry.Value.Clone();
            _lookup.Add(entry.Key, value);
            list.Add(new KeyValuePair<Iri, JsonElement>(entry.Key, value));
        }

        _entries = list.ToArray();
    }

    /// <summary>The empty extensions map.</summary>
    public static Extensions Empty { get; } = new([]);

    /// <inheritdoc/>
    public int Count => _entries.Length;

    /// <summary>The keys, in insertion order.</summary>
    public IEnumerable<Iri> Keys => _entries.Select(e => e.Key);

    /// <summary>The values, in insertion order.</summary>
    public IEnumerable<JsonElement> Values => _entries.Select(e => e.Value);

    /// <summary>Gets the value for a key.</summary>
    /// <param name="key">The extension IRI.</param>
    /// <returns>The JSON value.</returns>
    /// <exception cref="KeyNotFoundException">The key is not in the map.</exception>
    public JsonElement this[Iri key] => _lookup[key];

    /// <summary>Equality operator.</summary>
    /// <param name="left">The first map.</param>
    /// <param name="right">The second map.</param>
    /// <returns><see langword="true"/> when both maps hold deeply equal entries.</returns>
    public static bool operator ==(Extensions? left, Extensions? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">The first map.</param>
    /// <param name="right">The second map.</param>
    /// <returns><see langword="true"/> when the maps differ.</returns>
    public static bool operator !=(Extensions? left, Extensions? right) => !(left == right);

    /// <summary>Creates a map with a single entry.</summary>
    /// <param name="key">The extension IRI.</param>
    /// <param name="value">The JSON value.</param>
    /// <returns>The map.</returns>
    public static Extensions Create(Iri key, JsonElement value) => new([new KeyValuePair<Iri, JsonElement>(key, value)]);

    /// <summary>Returns a new map with the entry added, or replaced when the key already exists.</summary>
    /// <param name="key">The extension IRI.</param>
    /// <param name="value">The JSON value.</param>
    /// <returns>The new map.</returns>
    public Extensions With(Iri key, JsonElement value) =>
        new(_entries.Where(e => e.Key != key).Append(new KeyValuePair<Iri, JsonElement>(key, value)));

    /// <inheritdoc/>
    public bool ContainsKey(Iri key) => _lookup.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(Iri key, [MaybeNullWhen(false)] out JsonElement value) => _lookup.TryGetValue(key, out value);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<Iri, JsonElement>> GetEnumerator() => ((IEnumerable<KeyValuePair<Iri, JsonElement>>)_entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public bool Equals(Extensions? other) => other is not null && Equals(other, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Extensions other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = _entries.Length;
        foreach (var entry in _entries)
        {
            hash ^= Hash.Combine(entry.Key.GetHashCode(), JsonValueComparer.GetHashCode(entry.Value));
        }

        return hash;
    }

    /// <summary>Returns the keys formatted as <c>{iri, iri}</c>.</summary>
    /// <returns>A diagnostic string.</returns>
    public override string ToString() => "{" + string.Join(", ", _entries.Select(e => e.Key.Value)) + "}";

    internal bool Equals(Extensions other, StringComparison comparison)
    {
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
            var match = Array.FindIndex(other._entries, e => string.Equals(e.Key.Value, entry.Key.Value, comparison));
            if (match < 0 || !JsonValueComparer.DeepEquals(entry.Value, other._entries[match].Value, comparison))
            {
                return false;
            }
        }

        return true;
    }
}
