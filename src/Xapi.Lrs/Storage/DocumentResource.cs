using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>The three document resources of the standard (IEEE 9274.1.1, 4.1.6.2, 4.1.6.5 and 4.1.6.6).</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public enum DocumentResource
{
    /// <summary>The State Resource, <c>/activities/state</c>: documents of an Agent in an Activity, optionally per registration.</summary>
    State,

    /// <summary>The Activity Profile Resource, <c>/activities/profile</c>: documents of an Activity.</summary>
    ActivityProfile,

    /// <summary>The Agent Profile Resource, <c>/agents/profile</c>: documents of an Agent.</summary>
    AgentProfile,
}
