namespace Xapi;

/// <summary>Contextual information that gives a Statement more meaning.</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.5 "Context Table".</remarks>
public sealed record Context
{
    private readonly XapiList<ContextAgent> _contextAgents = XapiList<ContextAgent>.Empty;
    private readonly XapiList<ContextGroup> _contextGroups = XapiList<ContextGroup>.Empty;

    /// <summary>The registration (attempt or enrollment) the Statement belongs to.</summary>
    public Guid? Registration { get; init; }

    /// <summary>The instructor. Deprecated in xAPI 2.0 in favour of <see cref="ContextAgents"/>.</summary>
    public Actor? Instructor { get; init; }

    /// <summary>The team. Deprecated in xAPI 2.0 in favour of <see cref="ContextGroups"/>.</summary>
    public Group? Team { get; init; }

    /// <summary>Agents related to the Statement (xAPI 2.0 only).</summary>
    public XapiList<ContextAgent> ContextAgents
    {
        get => _contextAgents;
        init => _contextAgents = value ?? XapiList<ContextAgent>.Empty;
    }

    /// <summary>Groups related to the Statement (xAPI 2.0 only).</summary>
    public XapiList<ContextGroup> ContextGroups
    {
        get => _contextGroups;
        init => _contextGroups = value ?? XapiList<ContextGroup>.Empty;
    }

    /// <summary>Related Activities.</summary>
    public ContextActivities? ContextActivities { get; init; }

    /// <summary>The revision of the learning Activity. Only allowed when the Statement's object is an Activity.</summary>
    public string? Revision { get; init; }

    /// <summary>The platform used. Only allowed when the Statement's object is an Activity.</summary>
    public string? Platform { get; init; }

    /// <summary>The RFC 5646 code of the language the experience happened in.</summary>
    public string? Language { get; init; }

    /// <summary>Another Statement that gives context to this one.</summary>
    public StatementRef? Statement { get; init; }

    /// <summary>Extensions.</summary>
    public Extensions? Extensions { get; init; }
}
