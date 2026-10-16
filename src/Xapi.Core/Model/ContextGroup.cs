using Xapi.Internal;

namespace Xapi;

/// <summary>A Group related to a Statement, with the kinds of relationship (xAPI 2.0 <c>contextGroups</c>).</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.5 "Context Group Table". Not available in xAPI 1.0.3.</remarks>
public sealed record ContextGroup
{
    private readonly Group _group;
    private readonly XapiList<Iri> _relevantTypes = XapiList<Iri>.Empty;

    /// <summary>Creates a context group.</summary>
    /// <param name="group">The related Group.</param>
    public ContextGroup(Group group) => _group = Guard.NotNull(group);

    /// <summary>The related Group.</summary>
    public Group Group
    {
        get => _group;
        init => _group = Guard.NotNull(value);
    }

    /// <summary>IRIs describing the relationship. Empty means a generic relationship (not recommended).</summary>
    public XapiList<Iri> RelevantTypes
    {
        get => _relevantTypes;
        init => _relevantTypes = value ?? XapiList<Iri>.Empty;
    }
}
