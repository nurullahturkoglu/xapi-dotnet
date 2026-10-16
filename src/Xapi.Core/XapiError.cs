namespace Xapi;

/// <summary>One problem found while reading or validating xAPI data.</summary>
/// <param name="Code">A stable, lowercase dotted error code from <see cref="XapiErrorCodes"/>.</param>
/// <param name="Path">
/// A JSON path to the offending value, for example <c>$.context.contextAgents[0].relevantTypes</c>. Batch
/// errors start with the index, for example <c>$[3].id</c>.
/// </param>
/// <param name="Message">A human-readable English description. Not stable; match on <paramref name="Code"/>.</param>
public sealed record XapiError(string Code, string Path, string Message)
{
    /// <summary>Returns <c>path: message (code)</c>.</summary>
    /// <returns>A diagnostic string.</returns>
    public override string ToString() => $"{Path}: {Message} ({Code})";
}
