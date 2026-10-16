using Xapi.Internal;

namespace Xapi;

/// <summary>The response of the About resource: the xAPI versions an LRS supports, plus extensions.</summary>
/// <remarks>IEEE 9274.1.1, 4.1.6.7.</remarks>
public sealed record About
{
    private readonly XapiList<string> _version;

    /// <summary>Creates an About response.</summary>
    /// <param name="version">The supported xAPI versions, for example <c>1.0.3</c> and <c>2.0.0</c>.</param>
    public About(XapiList<string> version) => _version = Guard.NotNull(version);

    /// <summary>The supported xAPI versions (JSON <c>version</c>).</summary>
    public XapiList<string> Version
    {
        get => _version;
        init => _version = Guard.NotNull(value);
    }

    /// <summary>Extensions.</summary>
    public Extensions? Extensions { get; init; }
}
