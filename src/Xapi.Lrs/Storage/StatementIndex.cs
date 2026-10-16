namespace Xapi.Lrs.Storage;

// What a statement refers to: the link rows that the statement query filters run on (agent, related_agents,
// activity, related_activities), and the actors and activities whose definitions the LRS keeps.
internal sealed class StatementIndex
{
    private readonly HashSet<(short Usage, string Ifi, short Type)> _actorLinks = [];
    private readonly HashSet<(short Usage, string Iri)> _activityLinks = [];

    private StatementIndex()
    {
    }

    public IReadOnlyCollection<(short Usage, string Ifi, short Type)> ActorLinks => _actorLinks;

    public IReadOnlyCollection<(short Usage, string Iri)> ActivityLinks => _activityLinks;

    // Every identified Agent and Group in the statement, in document order, including Group members
    public List<Actor> Actors { get; } = [];

    // Every Activity in the statement, in document order
    public List<Activity> Activities { get; } = [];

    public static StatementIndex Build(Statement statement)
    {
        var index = new StatementIndex();
        index.AddActor(statement.Actor, ActorUsage.Actor);
        index.AddObject(statement.Object, 0);
        if (statement.Authority is { } authority)
        {
            index.AddActor(authority, ActorUsage.Authority);
        }

        index.AddContext(statement.Context, 0);
        return index;
    }

    private void AddObject(StatementObject value, short offset)
    {
        switch (value)
        {
            case Activity activity:
                AddActivity(activity, (short)(ActivityUsage.Object + offset));
                break;
            case Actor actor:
                AddActor(actor, (short)(ActorUsage.Object + offset));
                break;
            case SubStatement sub:
                // A SubStatement cannot contain another one, so the offset is applied once
                AddActor(sub.Actor, (short)(ActorUsage.Actor + Usage.SubStatementOffset));
                AddObject(sub.Object, Usage.SubStatementOffset);
                AddContext(sub.Context, Usage.SubStatementOffset);
                break;
            default:
                // StatementRef: recorded in xapi_statement.ref_statement_id and xapi_statement_ref
                break;
        }
    }

    private void AddContext(Context? context, short offset)
    {
        if (context is null)
        {
            return;
        }

        if (context.Instructor is { } instructor)
        {
            AddActor(instructor, (short)(ActorUsage.Instructor + offset));
        }

        if (context.Team is { } team)
        {
            AddActor(team, (short)(ActorUsage.Team + offset));
        }

        foreach (var contextAgent in context.ContextAgents)
        {
            AddActor(contextAgent.Agent, (short)(ActorUsage.ContextAgent + offset));
        }

        foreach (var contextGroup in context.ContextGroups)
        {
            AddActor(contextGroup.Group, (short)(ActorUsage.ContextGroup + offset));
        }

        if (context.ContextActivities is { } activities)
        {
            AddActivities(activities.Parent, (short)(ActivityUsage.Parent + offset));
            AddActivities(activities.Grouping, (short)(ActivityUsage.Grouping + offset));
            AddActivities(activities.Category, (short)(ActivityUsage.Category + offset));
            AddActivities(activities.Other, (short)(ActivityUsage.Other + offset));
        }
    }

    private void AddActivities(XapiList<Activity> activities, short usage)
    {
        foreach (var activity in activities)
        {
            AddActivity(activity, usage);
        }
    }

    private void AddActivity(Activity activity, short usage)
    {
        _activityLinks.Add((usage, activity.Id.Value));
        Activities.Add(activity);
    }

    private void AddActor(Actor actor, short usage)
    {
        switch (actor)
        {
            case Agent agent:
                _actorLinks.Add((usage, IfiKeys.For(agent.Ifi), ActorType.Agent));
                Actors.Add(agent);
                break;
            case Group group:
                if (group.Ifi is { } ifi)
                {
                    _actorLinks.Add((usage, IfiKeys.For(ifi), ActorType.Group));
                    Actors.Add(group);
                }

                // Members get rows under the Group's usage, so an agent filter matches statements by a Group the
                // agent belongs to (IEEE 9274.1.1, 4.1.6.1, GET "agent" parameter)
                foreach (var member in group.Members)
                {
                    _actorLinks.Add((usage, IfiKeys.For(member.Ifi), ActorType.Agent));
                    Actors.Add(member);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(actor), actor.GetType().Name, "Unknown actor type.");
        }
    }
}
