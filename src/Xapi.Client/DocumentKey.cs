using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>
/// Identifies one document in the State, Activity Profile or Agent Profile resource: <see cref="StateKey"/>,
/// <see cref="ActivityProfileKey"/> or <see cref="AgentProfileKey"/>.
/// </summary>
public abstract record DocumentKey
{
    private protected DocumentKey()
    {
    }

    /// <summary>The resource path relative to the endpoint, for example <c>activities/state</c>.</summary>
    internal abstract string Resource { get; }

    internal abstract void AddParameters(QueryBuilder query, XapiVersion version);
}
