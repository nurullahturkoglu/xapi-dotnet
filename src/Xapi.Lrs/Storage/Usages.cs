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
}

internal static class ActivityUsage
{
    public const short Object = 1;
    public const short Parent = 2;
    public const short Grouping = 3;
    public const short Category = 4;
    public const short Other = 5;
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
