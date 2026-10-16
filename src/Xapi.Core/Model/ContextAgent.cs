using Xapi.Internal;

namespace Xapi;

/// <summary>An Agent related to a Statement, with the kinds of relationship (xAPI 2.0 <c>contextAgents</c>).</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.5 "Context Agents Table". Not available in xAPI 1.0.3.</remarks>
public sealed record ContextAgent
{
    private readonly Agent _agent;
    private readonly XapiList<Iri> _relevantTypes = XapiList<Iri>.Empty;

    /// <summary>Creates a context agent.</summary>
    /// <param name="agent">The related Agent.</param>
    public ContextAgent(Agent agent) => _agent = Guard.NotNull(agent);

    /// <summary>The related Agent.</summary>
    public Agent Agent
    {
        get => _agent;
        init => _agent = Guard.NotNull(value);
    }

    /// <summary>IRIs describing the relationship. Empty means a generic relationship (not recommended).</summary>
    public XapiList<Iri> RelevantTypes
    {
        get => _relevantTypes;
        init => _relevantTypes = value ?? XapiList<Iri>.Empty;
    }
}
