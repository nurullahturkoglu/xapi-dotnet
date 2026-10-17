namespace Xapi.Lrs.Storage;

// Stored as smallint. The values are part of the schema: never renumber, only add.
// Usages inside a SubStatement are the top-level value plus SubStatementOffset.

internal static class ActorUsage
{
    public const short Actor = 1;
    public const short Object = 2;
    public const short Authority = 3;
    public const short Instructor = 4;
    public const short Team = 5;
    public const short ContextAgent = 6;
    public const short ContextGroup = 7;

    // Every usage StatementIndex writes; the related_agents filter reads each as its own index range
    public static readonly short[] All =
    [
        Actor, Object, Authority, Instructor, Team, ContextAgent, ContextGroup,
        Actor + Usage.SubStatementOffset, Object + Usage.SubStatementOffset, Instructor + Usage.SubStatementOffset,
        Team + Usage.SubStatementOffset, ContextAgent + Usage.SubStatementOffset, ContextGroup + Usage.SubStatementOffset,
    ];

    // The agent filter without related_agents
    public static readonly short[] ActorOrObject = [Actor, Object];
}

internal static class ActivityUsage
{
    public const short Object = 1;
    public const short Parent = 2;
    public const short Grouping = 3;
    public const short Category = 4;
    public const short Other = 5;

    // Every usage StatementIndex writes; the related_activities filter reads each as its own index range
    public static readonly short[] All =
    [
        Object, Parent, Grouping, Category, Other,
        Object + Usage.SubStatementOffset, Parent + Usage.SubStatementOffset, Grouping + Usage.SubStatementOffset,
        Category + Usage.SubStatementOffset, Other + Usage.SubStatementOffset,
    ];

    // The activity filter without related_activities
    public static readonly short[] ObjectOnly = [Object];
}

internal static class ActorType
{
    public const short Agent = 1;
    public const short Group = 2;
}

internal static class Usage
{
    public const short SubStatementOffset = 100;
}
