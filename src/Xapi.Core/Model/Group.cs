using Xapi.Internal;

namespace Xapi;

/// <summary>
/// A Group of Agents. An identified Group has an inverse functional identifier and may list members; an
/// anonymous Group has no identifier and must list at least one member.
/// </summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.1 "Actor as Anonymous Group Table" and "Actor as Identified Group Table".</remarks>
public sealed record Group : Actor
{
    private readonly XapiList<Agent> _members = XapiList<Agent>.Empty;

    /// <summary>Creates an anonymous Group.</summary>
    /// <param name="members">The members; must not be empty for the Group to be valid.</param>
    public Group(XapiList<Agent> members) => _members = Guard.NotNull(members);

    /// <summary>Creates an identified Group without listed members.</summary>
    /// <param name="ifi">The Group's identifier.</param>
    public Group(InverseFunctionalIdentifier ifi) => Ifi = Guard.NotNull(ifi);

    /// <summary>The identifier, or <see langword="null"/> for an anonymous Group.</summary>
    public InverseFunctionalIdentifier? Ifi { get; init; }

    /// <summary>The members (JSON <c>member</c>). Unordered as far as the standard is concerned.</summary>
    public XapiList<Agent> Members
    {
        get => _members;
        init => _members = value ?? XapiList<Agent>.Empty;
    }

    /// <summary>Whether the Group has no identifier.</summary>
    public bool IsAnonymous => Ifi is null;
}
