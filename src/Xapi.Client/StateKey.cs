using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>A document in the State resource (<c>activities/state</c>), IEEE 9274.1.1, 4.1.6.2.</summary>
public sealed record StateKey : DocumentKey
{
    private readonly Iri _activityId;
    private readonly Agent _agent;
    private readonly string _stateId;

    /// <summary>Creates a key.</summary>
    /// <param name="activityId">The Activity the state belongs to (<c>activityId</c>).</param>
    /// <param name="agent">The Agent the state belongs to (<c>agent</c>).</param>
    /// <param name="stateId">The document id within that context (<c>stateId</c>).</param>
    /// <param name="registration">The registration, if the state is tied to one (<c>registration</c>).</param>
    public StateKey(Iri activityId, Agent agent, string stateId, Guid? registration = null)
    {
        _activityId = Guard.NotDefault(activityId);
        _agent = Guard.NotNull(agent);
        _stateId = Guard.NotEmpty(stateId);
        Registration = registration;
    }

    /// <summary>The Activity the state belongs to (<c>activityId</c>).</summary>
    public Iri ActivityId
    {
        get => _activityId;
        init => _activityId = Guard.NotDefault(value);
    }

    /// <summary>The Agent the state belongs to (<c>agent</c>).</summary>
    public Agent Agent
    {
        get => _agent;
        init => _agent = Guard.NotNull(value);
    }

    /// <summary>The document id within that context (<c>stateId</c>).</summary>
    public string StateId
    {
        get => _stateId;
        init => _stateId = Guard.NotEmpty(value);
    }

    /// <summary>The registration, if the state is tied to one (<c>registration</c>).</summary>
    public Guid? Registration { get; init; }

    internal override string Resource => XapiClient.StatesResource;

    internal override void AddParameters(QueryBuilder query, XapiVersion version) =>
        query.Add("activityId", ActivityId)
            .Add("agent", Agent, version)
            .Add("registration", Registration)
            .Add("stateId", StateId);
}
