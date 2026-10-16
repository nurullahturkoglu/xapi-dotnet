using System.Collections;
using System.Runtime.CompilerServices;
using Xapi.Internal;

namespace Xapi;

/// <summary>
/// An immutable list with structural (element-by-element, ordered) equality, so that records holding lists
/// compare by value.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
[CollectionBuilder(typeof(XapiList), nameof(XapiList.Create))]
public sealed class XapiList<T> : IReadOnlyList<T>, IEquatable<XapiList<T>>
{
    private readonly T[] _items;

    internal XapiList(T[] items) => _items = items;

    /// <summary>The empty list.</summary>
    public static XapiList<T> Empty { get; } = new([]);

    /// <inheritdoc/>
    public int Count => _items.Length;

    /// <summary>Whether the list has no elements.</summary>
    public bool IsEmpty => _items.Length == 0;

    /// <inheritdoc/>
    public T this[int index] => _items[index];

    /// <summary>Equality operator.</summary>
    /// <param name="left">The first list.</param>
    /// <param name="right">The second list.</param>
    /// <returns><see langword="true"/> when both lists hold equal elements in the same order.</returns>
    public static bool operator ==(XapiList<T>? left, XapiList<T>? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">The first list.</param>
    /// <param name="right">The second list.</param>
    /// <returns><see langword="true"/> when the lists differ.</returns>
    public static bool operator !=(XapiList<T>? left, XapiList<T>? right) => !(left == right);

    /// <summary>Returns a new list with <paramref name="item"/> appended.</summary>
    /// <param name="item">The element to append.</param>
    /// <returns>The new list.</returns>
    public XapiList<T> Add(T item)
    {
        var items = new T[_items.Length + 1];
        Array.Copy(_items, items, _items.Length);
        items[_items.Length] = item;
        return new XapiList<T>(items);
    }

    /// <summary>Returns a struct enumerator over the elements.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(_items);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    public bool Equals(XapiList<T>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_items.Length != other._items.Length)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < _items.Length; i++)
        {
            if (!comparer.Equals(_items[i], other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is XapiList<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = _items.Length;
        foreach (var item in _items)
        {
            hash = Hash.Combine(hash, Hash.Of(item));
        }

        return hash;
    }

    /// <summary>Returns the elements formatted as <c>[a, b]</c>.</summary>
    /// <returns>A diagnostic string.</returns>
    public override string ToString() => "[" + string.Join(", ", _items) + "]";

    /// <summary>A struct enumerator for <see cref="XapiList{T}"/>.</summary>
    public struct Enumerator
    {
        private readonly T[] _items;
        private int _index;

        internal Enumerator(T[] items)
        {
            _items = items;
            _index = -1;
        }

        /// <summary>The current element.</summary>
        public readonly T Current => _items[_index];

        /// <summary>Advances to the next element.</summary>
        /// <returns><see langword="false"/> after the last element.</returns>
        public bool MoveNext() => ++_index < _items.Length;
    }
}

/// <summary>Factory methods for <see cref="XapiList{T}"/>, usable from C# 9 and as a collection-expression builder.</summary>
public static class XapiList
{
    /// <summary>Creates a list from the given elements.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The elements, copied.</param>
    /// <returns>The list.</returns>
    public static XapiList<T> Create<T>(params T[] items)
    {
        Guard.NotNull(items);
        return items.Length == 0 ? XapiList<T>.Empty : new XapiList<T>((T[])items.Clone());
    }

    /// <summary>Creates a list from a span. This is the collection-expression builder (<c>[a, b]</c>).</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The elements, copied.</param>
    /// <returns>The list.</returns>
    public static XapiList<T> Create<T>(ReadOnlySpan<T> items) =>
        items.IsEmpty ? XapiList<T>.Empty : new XapiList<T>(items.ToArray());

    /// <summary>Creates a list from a sequence.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The elements, copied.</param>
    /// <returns>The list.</returns>
    public static XapiList<T> CreateRange<T>(IEnumerable<T> items)
    {
        Guard.NotNull(items);
        if (items is XapiList<T> list)
        {
            return list;
        }

        var array = items.ToArray();
        return array.Length == 0 ? XapiList<T>.Empty : new XapiList<T>(array);
    }
}
