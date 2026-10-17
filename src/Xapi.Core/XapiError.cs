namespace Xapi;

/// <summary>One problem found while reading or validating xAPI data.</summary>
public sealed record XapiError
{
    /// <summary>Creates an error.</summary>
    /// <param name="code">A stable, lowercase dotted error code from <see cref="XapiErrorCodes"/>.</param>
    /// <param name="path">A JSON path to the offending value; see <see cref="Path"/>.</param>
    /// <param name="message">A human-readable English description.</param>
    public XapiError(string code, string path, string message)
    {
        Code = code;
        Path = path;
        Message = message;
    }

    /// <summary>A stable, lowercase dotted error code from <see cref="XapiErrorCodes"/>.</summary>
    public string Code { get; init; }

    /// <summary>
    /// A JSON path to the offending value, for example <c>$.context.contextAgents[0].relevantTypes</c>. Batch
    /// errors start with the index, for example <c>$[3].id</c>. The one exception is
    /// <see cref="XapiErrorCodes.AttachmentPayloadUnreferenced"/>, whose path is an index into the attachment data
    /// sent with the Statements, such as <c>attachments[0]</c>, because that data is not part of the JSON.
    /// </summary>
    public string Path { get; init; }

    /// <summary>A human-readable English description. Not stable; match on <see cref="Code"/>.</summary>
    public string Message { get; init; }

    /// <summary>Returns <c>path: message (code)</c>.</summary>
    /// <returns>A diagnostic string.</returns>
    public override string ToString() => $"{Path}: {Message} ({Code})";
}
