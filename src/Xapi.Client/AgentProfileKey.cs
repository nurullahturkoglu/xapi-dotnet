using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>A document in the Agent Profile resource (<c>agents/profile</c>), IEEE 9274.1.1, 4.1.6.5.</summary>
public sealed record AgentProfileKey : DocumentKey
{
    private readonly Agent _agent;
    private readonly string _profileId;

    /// <summary>Creates a key.</summary>
    /// <param name="agent">The Agent the profile belongs to (<c>agent</c>).</param>
    /// <param name="profileId">The document id (<c>profileId</c>).</param>
    public AgentProfileKey(Agent agent, string profileId)
    {
        _agent = Guard.NotNull(agent);
        _profileId = Guard.NotEmpty(profileId);
    }

    /// <summary>The Agent the profile belongs to (<c>agent</c>).</summary>
    public Agent Agent
    {
        get => _agent;
        init => _agent = Guard.NotNull(value);
    }

    /// <summary>The document id (<c>profileId</c>).</summary>
    public string ProfileId
    {
        get => _profileId;
        init => _profileId = Guard.NotEmpty(value);
    }

    internal override string Resource => XapiClient.AgentProfilesResource;

    internal override void AddParameters(QueryBuilder query, XapiVersion version) =>
        query.Add("agent", Agent, version).Add("profileId", ProfileId);
}
