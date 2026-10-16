namespace Xapi;

/// <summary>Activities that give a Statement context, grouped by their relationship to it.</summary>
/// <remarks>
/// IEEE 9274.1.1, 4.2.2.5. A single Activity object in JSON is read as a list of one, and lists are always
/// written as arrays (4.2.4.2). Empty lists are not written.
/// </remarks>
public sealed record ContextActivities
{
    private readonly XapiList<Activity> _parent = XapiList<Activity>.Empty;
    private readonly XapiList<Activity> _grouping = XapiList<Activity>.Empty;
    private readonly XapiList<Activity> _category = XapiList<Activity>.Empty;
    private readonly XapiList<Activity> _other = XapiList<Activity>.Empty;

    /// <summary>Activities the Statement's object is directly part of.</summary>
    public XapiList<Activity> Parent
    {
        get => _parent;
        init => _parent = value ?? XapiList<Activity>.Empty;
    }

    /// <summary>Activities with an indirect relationship.</summary>
    public XapiList<Activity> Grouping
    {
        get => _grouping;
        init => _grouping = value ?? XapiList<Activity>.Empty;
    }

    /// <summary>Activities used to categorize the Statement, such as a profile.</summary>
    public XapiList<Activity> Category
    {
        get => _category;
        init => _category = value ?? XapiList<Activity>.Empty;
    }

    /// <summary>Other contextual Activities.</summary>
    public XapiList<Activity> Other
    {
        get => _other;
        init => _other = value ?? XapiList<Activity>.Empty;
    }
}
