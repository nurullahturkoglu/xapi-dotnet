using System.Globalization;

namespace Xapi.Internal;

internal static class JsonPath
{
    public const string Root = "$";

    public static string Property(string parent, string name) =>
        IsIdentifier(name)
            ? parent + "." + name
            : parent + "['" + name.Replace("\\", "\\\\").Replace("'", "\\'") + "']";

    public static string Index(string parent, int index) =>
        parent + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

    private static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || XapiFormats.IsDigit(name[0]))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(XapiFormats.IsAsciiLetter(c) || XapiFormats.IsDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The segments of the JSON path a reader or validator is at, kept so that a path is only turned into a string
/// when an error needs it.
/// </summary>
/// <remarks>
/// Reading and validating walk the tree depth first, so the path to the current value is always a prefix of the
/// segments here, and a <see cref="JsonPathRef"/> stays usable until a segment it depends on is replaced by a
/// sibling's. Each segment carries the order in which it was pushed, so using a path after that throws instead of
/// returning a wrong string. Not thread-safe: each reader or validator run owns one.
/// </remarks>
internal sealed class JsonPathStack
{
    private Segment[] _segments = new Segment[8];
    private long _pushes;

    public JsonPathRef Root => new(this, 0, 0);

    internal JsonPathRef Push(int depth, long parentStamp, string? name, int index)
    {
        if (depth > 0 && _segments[depth - 1].Stamp != parentStamp)
        {
            throw Stale();
        }

        if (depth == _segments.Length)
        {
            Array.Resize(ref _segments, depth * 2);
        }

        var stamp = ++_pushes;
        _segments[depth] = new Segment(name, index, stamp);
        return new JsonPathRef(this, depth + 1, stamp);
    }

    internal string Format(int depth, long stamp)
    {
        var path = JsonPath.Root;
        var previous = 0L;
        for (var i = 0; i < depth; i++)
        {
            var segment = _segments[i];
            if (segment.Stamp <= previous)
            {
                throw Stale();
            }

            previous = segment.Stamp;
            path = segment.Name is null ? JsonPath.Index(path, segment.Index) : JsonPath.Property(path, segment.Name);
        }

        return previous == stamp ? path : throw Stale();
    }

    private static InvalidOperationException Stale() =>
        new("A JSON path was used after the reader moved past it.");

    private readonly struct Segment(string? name, int index, long stamp)
    {
        public string? Name { get; } = name;

        public int Index { get; } = index;

        public long Stamp { get; } = stamp;
    }
}

/// <summary>A JSON path on a <see cref="JsonPathStack"/>; <see cref="ToString"/> builds the string.</summary>
internal readonly struct JsonPathRef
{
    private readonly JsonPathStack _stack;
    private readonly long _stamp;

    internal JsonPathRef(JsonPathStack stack, int depth, long stamp)
    {
        _stack = stack;
        Depth = depth;
        _stamp = stamp;
    }

    /// <summary>The number of segments after <c>$</c>.</summary>
    public int Depth { get; }

    public JsonPathRef Property(string name) => _stack.Push(Depth, _stamp, name, 0);

    public JsonPathRef Index(int index) => _stack.Push(Depth, _stamp, null, index);

    /// <summary>The path in the form <see cref="JsonPath"/> writes, for example <c>$.context.contextActivities.parent[0]</c>.</summary>
    /// <returns>The path.</returns>
    public override string ToString() => _stack.Format(Depth, _stamp);
}
