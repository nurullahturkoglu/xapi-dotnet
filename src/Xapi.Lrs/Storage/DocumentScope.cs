using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>
/// The context a document lives in: an Activity and Agent (and optionally a registration) for state, an
/// Activity for activity profiles, an Agent for agent profiles. Documents are identified by their id within
/// a scope.
/// </summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public sealed class DocumentScope
{
    private DocumentScope(DocumentResource resource, Iri? activityId, Agent? agent, Guid? registration)
    {
        Resource = resource;
        ActivityId = activityId;
        Agent = agent;
        Registration = registration;
    }

    /// <summary>The resource the documents belong to.</summary>
    public DocumentResource Resource { get; }

    /// <summary>The Activity, for state and activity profiles; otherwise <see langword="null"/>.</summary>
    public Iri? ActivityId { get; }

    /// <summary>The Agent, for state and agent profiles; otherwise <see langword="null"/>. Only its identifier counts.</summary>
    public Agent? Agent { get; }

    /// <summary>
    /// The registration of state documents. For a single document, <see langword="null"/> means the document
    /// stored without a registration. When listing or deleting all state documents, <see langword="null"/>
    /// means every registration (IEEE 9274.1.1, 4.1.6.2: "Activity + Agent [+ registration if specified]").
    /// </summary>
    public Guid? Registration { get; }

    /// <summary>The scope of state documents.</summary>
    /// <param name="activityId">The Activity.</param>
    /// <param name="agent">The Agent.</param>
    /// <param name="registration">The registration, if any.</param>
    /// <returns>The scope.</returns>
    public static DocumentScope ForState(Iri activityId, Agent agent, Guid? registration = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new(DocumentResource.State, NotEmpty(activityId), agent, registration);
    }

    /// <summary>The scope of the profile documents of an Activity.</summary>
    /// <param name="activityId">The Activity.</param>
    /// <returns>The scope.</returns>
    public static DocumentScope ForActivityProfile(Iri activityId) =>
        new(DocumentResource.ActivityProfile, NotEmpty(activityId), agent: null, registration: null);

    /// <summary>The scope of the profile documents of an Agent.</summary>
    /// <param name="agent">The Agent.</param>
    /// <returns>The scope.</returns>
    public static DocumentScope ForAgentProfile(Agent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new(DocumentResource.AgentProfile, activityId: null, agent, registration: null);
    }

    private static Iri NotEmpty(Iri activityId) =>
        activityId.IsEmpty ? throw new ArgumentException("The Activity id is empty.", nameof(activityId)) : activityId;
}
