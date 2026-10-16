namespace Xapi.Json;

internal static class InteractionTypes
{
    public static readonly string[] Names =
        ["true-false", "choice", "fill-in", "long-fill-in", "matching", "performance", "sequencing", "likert", "numeric", "other"];

    public static bool TryParse(string text, out InteractionType type)
    {
        // Enumerated values are case-sensitive (4.2.1).
        var index = Array.IndexOf(Names, text);
        type = (InteractionType)Math.Max(index, 0);
        return index >= 0;
    }

    public static string ToJson(InteractionType type) => Names[(int)type];
}
