using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>A document in the State resource (<c>activities/state</c>), IEEE 9274.1.1, 4.1.6.2.</summary>
/// <param name="ActivityId">The Activity the state belongs to (<c>activityId</c>).</param>
/// <param name="Agent">The Agent the state belongs to (<c>agent</c>).</param>
/// <param name="StateId">The document id within that context (<c>stateId</c>).</param>
/// <param name="Registration">The registration, if the state is tied to one (<c>registration</c>).</param>
public sealed record StateKey(Iri ActivityId, Agent Agent, string StateId, Guid? Registration = null) : DocumentKey
{
    private readonly Iri _activityId = Guard.NotDefault(ActivityId);
    private readonly Agent _agent = Guard.NotNull(Agent);
    private readonly string _stateId = Guard.NotEmpty(StateId);

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

    internal override string Resource => XapiClient.StatesResource;

    internal override void AddParameters(QueryBuilder query, XapiVersion version) =>
        query.Add("activityId", ActivityId)
            .Add("agent", Agent, version)
            .Add("registration", Registration)
            .Add("stateId", StateId);
}
