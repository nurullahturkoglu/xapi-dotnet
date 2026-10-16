using System.Text.Json;

namespace Xapi.Internal;

/// <summary>
/// Deep JSON value comparison: objects as unordered property sets, arrays in order, numbers by value.
/// Written by hand because JsonElement.DeepEquals is not part of every target and cannot ignore case.
/// </summary>
internal static class JsonValueComparer
{
    public static bool DeepEquals(JsonElement left, JsonElement right, StringComparison stringComparison = StringComparison.Ordinal)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                return ObjectEquals(left, right, stringComparison);
            case JsonValueKind.Array:
                if (left.GetArrayLength() != right.GetArrayLength())
                {
                    return false;
                }

                using (var l = left.EnumerateArray())
                using (var r = right.EnumerateArray())
                {
                    while (l.MoveNext() && r.MoveNext())
                    {
                        if (!DeepEquals(l.Current, r.Current, stringComparison))
                        {
                            return false;
                        }
                    }
                }

                return true;
            case JsonValueKind.String:
                return string.Equals(left.GetString(), right.GetString(), stringComparison);
            case JsonValueKind.Number:
                return NumberEquals(left, right);
            default:
                return true;
        }
    }

    public static int GetHashCode(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => Hash.Combine((int)element.ValueKind, element.EnumerateObject().Count()),
            JsonValueKind.Array => Hash.Combine((int)element.ValueKind, element.GetArrayLength()),
            _ => (int)element.ValueKind,
        };

    private static bool ObjectEquals(JsonElement left, JsonElement right, StringComparison stringComparison)
    {
        var rightProperties = new List<JsonProperty>();
        foreach (var property in right.EnumerateObject())
        {
            rightProperties.Add(property);
        }

        var count = 0;
        foreach (var property in left.EnumerateObject())
        {
            count++;
            var match = rightProperties.FindIndex(p => string.Equals(p.Name, property.Name, stringComparison));
            if (match < 0 || !DeepEquals(property.Value, rightProperties[match].Value, stringComparison))
            {
                return false;
            }
        }

        return count == rightProperties.Count;
    }

    private static bool NumberEquals(JsonElement left, JsonElement right)
    {
        if (left.TryGetDecimal(out var l) && right.TryGetDecimal(out var r))
        {
            return l == r;
        }

        if (left.TryGetDouble(out var ld) && right.TryGetDouble(out var rd))
        {
            return ld.Equals(rd);
        }

        return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
}
