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
