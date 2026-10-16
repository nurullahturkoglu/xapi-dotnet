using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xapi.Json;
using Xapi.Validation;

namespace Xapi.Lrs.Storage;

internal sealed partial class EfStatementStore(
    IDbContextFactory<XapiLrsDbContext> contextFactory,
    XapiLrsDatabaseProvider provider,
    ILogger<EfStatementStore> logger) : IStatementStore
{
    private const int MaxAttempts = 8;

    private static readonly string VoidedVerb = Verbs.VoidedId.Value;

    public async Task<StatementStoreResult> StoreAsync(IReadOnlyList<StatementRecord> statements, bool updateDefinitions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statements);
        if (statements.Count == 0)
        {
            return StatementStoreResult.Stored;
        }

        // A concurrent writer of the same statement id or activity makes one transaction fail on a unique key
        // or a serialization check. Running the whole batch again then sees the other write and decides
        // between "equivalent, nothing to do" and 409.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TryStoreAsync(statements, updateDefinitions, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < MaxAttempts && provider.IsTransientWriteFailure(exception))
            {
                LogRetry(logger, attempt, exception);
                var delay = TimeSpan.FromMilliseconds((5 << attempt) + Random.Shared.Next(0, 10 << attempt));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<StoredStatement?> GetAsync(Guid statementId, CancellationToken cancellationToken = default)
    {
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var row = await context.Statements.AsNoTracking()
                .Where(s => s.StatementId == statementId)
                .Select(s => new { s.StatementId, s.Stored, s.Version, s.IsVoided, s.AuthorityKey, s.Payload })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return row is null
                ? null
                : new StoredStatement(row.StatementId, row.Stored, row.Version, row.IsVoided, row.AuthorityKey, row.Payload);
        }
    }

    public async Task<StatementPage> QueryAsync(StatementFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(filter.Limit, 1);

        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            // IEEE 9274.1.1, 4.1.6.1 "Voided Statements": never returned by a query
            var query = context.Statements.AsNoTracking().Where(s => !s.IsVoided);
            if (filter.AuthorityKey is { } authority)
            {
                query = query.Where(s => s.AuthorityKey == authority);
            }

            // Time and paging bounds apply to the returned statement, not to the statements it targets
            if (filter.Since is { } since)
            {
                var first = StatementKeys.FirstAfter(since);
                query = query.Where(s => s.Pk.CompareTo(first) >= 0);
            }

            if (filter.Until is { } until)
            {
                var end = StatementKeys.FirstAfter(until);
                query = query.Where(s => s.Pk.CompareTo(end) < 0);
            }

            if (filter.After is { } after)
            {
                query = filter.Ascending
                    ? query.Where(s => s.Pk.CompareTo(after) > 0)
                    : query.Where(s => s.Pk.CompareTo(after) < 0);
            }

            if (Matches(context, filter) is { } match)
            {
                // 4.1.6.1 "Filter Conditions for StatementRefs": a statement also matches when a statement it
                // targets matches, recursively (xapi_statement_ref holds every id reached), and voided targets
                // count (4.1.6.1 "Voided Statements": statements targeting a voided statement are returned)
                var matching = context.Statements.Where(match);
                Expression<Func<StatementEntity, bool>> targetsMatch = s =>
                    context.StatementRefs.Any(r => r.AncestorPk == s.Pk && matching.Any(t => t.StatementId == r.DescendantId));
                query = query.Where(Or(match, targetsMatch));
            }

            query = filter.Ascending ? query.OrderBy(s => s.Pk) : query.OrderByDescending(s => s.Pk);
            var rows = await query
                .Take(filter.Limit + 1)
                .Select(s => new { s.Pk, s.StatementId, s.Stored, s.Version, s.IsVoided, s.AuthorityKey, s.Payload })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var more = rows.Count > filter.Limit;
            if (more)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            var statements = rows.ConvertAll(r => new StoredStatement(r.StatementId, r.Stored, r.Version, r.IsVoided, r.AuthorityKey, r.Payload));
            return new StatementPage(statements, more ? rows[^1].Pk : null);
        }
    }

    public async Task<IReadOnlyDictionary<Iri, Activity>> GetActivitiesAsync(IReadOnlyCollection<Iri> activityIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityIds);
        if (activityIds.Count == 0)
        {
            return new Dictionary<Iri, Activity>();
        }

        var iris = activityIds.Select(i => i.Value).Distinct(StringComparer.Ordinal).ToList();
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var rows = await context.Activities.AsNoTracking()
                .Where(a => iris.Contains(a.Iri))
                .Select(a => a.Payload)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return rows.Select(ReadActivity).ToDictionary(a => a.Id);
        }
    }

    public async Task<Person> GetPersonAsync(Agent agent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);

        // An Agent has exactly one identifier and nothing in a statement links two identifiers to one person, so
        // the Person is built from the single actor row of that identifier. A Group row with the same identifier
        // describes the Group, not a person, and is left out.
        var ifi = IfiKeys.For(agent.Ifi);
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var payload = await context.Actors.AsNoTracking()
                .Where(a => a.Ifi == ifi && a.Type == ActorType.Agent)
                .Select(a => a.Payload)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var name = payload is null ? null : ReadActor(payload).Name;
            return PersonOf(agent.Ifi, name);
        }
    }

    private static Person PersonOf(InverseFunctionalIdentifier ifi, string? name)
    {
        var person = ifi switch
        {
            Mbox mbox => new Person { Mboxes = [mbox] },
            MboxSha1Sum sha1 => new Person { MboxSha1Sums = [sha1] },
            OpenId openId => new Person { OpenIds = [openId] },
            Account account => new Person { Accounts = [account] },
            _ => throw new ArgumentOutOfRangeException(nameof(ifi), ifi.GetType().Name, "Unknown identifier type."),
        };
        return name is null ? person : person with { Names = [name] };
    }

    // The content filters, as a condition on one statement row, or null when the query has none
    private static Expression<Func<StatementEntity, bool>>? Matches(XapiLrsDbContext context, StatementFilter filter)
    {
        var conditions = new List<Expression<Func<StatementEntity, bool>>>();
        if (filter.Verb is { } verb)
        {
            var iri = verb.Value;
            conditions.Add(t => t.VerbIri == iri);
        }

        if (filter.Registration is { } registration)
        {
            conditions.Add(t => t.Registration == registration);
        }

        if (filter.Activity is { } activity)
        {
            var iri = activity.Value;
            conditions.Add(filter.RelatedActivities
                ? t => context.StatementActivities.Any(a => a.StatementPk == t.Pk && a.ActivityIri == iri)
                : t => context.StatementActivities.Any(a => a.StatementPk == t.Pk && a.ActivityIri == iri && a.Usage == ActivityUsage.Object));
        }

        if (filter.Agent is { } agent)
        {
            // Group members have rows under the Group's usage (StatementIndex), so a member matches its Group
            var ifi = agent switch
            {
                Agent a => IfiKeys.For(a.Ifi),
                Group { Ifi: { } groupIfi } => IfiKeys.For(groupIfi),
                _ => throw new ArgumentException("The agent filter must be an Agent or an identified Group.", nameof(filter)),
            };
            conditions.Add(filter.RelatedAgents
                ? t => context.StatementActors.Any(a => a.StatementPk == t.Pk && a.ActorIfi == ifi)
                : t => context.StatementActors.Any(a => a.StatementPk == t.Pk && a.ActorIfi == ifi &&
                                                       (a.Usage == ActorUsage.Actor || a.Usage == ActorUsage.Object)));
        }

        return conditions.Count == 0 ? null : conditions.Aggregate((left, right) => Combine(left, right, Expression.AndAlso));
    }

    private static Expression<Func<StatementEntity, bool>> Or(Expression<Func<StatementEntity, bool>> left, Expression<Func<StatementEntity, bool>> right) =>
        Combine(left, right, Expression.OrElse);

    private static Expression<Func<StatementEntity, bool>> Combine(
        Expression<Func<StatementEntity, bool>> left,
        Expression<Func<StatementEntity, bool>> right,
        Func<Expression, Expression, BinaryExpression> combine)
    {
        // A new parameter: the same condition also appears as its own lambda inside the combined one (the
        // StatementRef subquery), and EF Core rejects one parameter object declared by two lambdas
        var parameter = Expression.Parameter(typeof(StatementEntity), "s");
        var leftBody = new ParameterReplacer(left.Parameters[0], parameter).Visit(left.Body);
        var rightBody = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<StatementEntity, bool>>(combine(leftBody, rightBody), parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    private async Task<StatementStoreResult> TryStoreAsync(IReadOnlyList<StatementRecord> statements, bool updateDefinitions, CancellationToken cancellationToken)
    {
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var transaction = await provider.BeginWriteTransactionAsync(context, cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var ids = statements.Select(s => s.Id).ToList();
                var existing = await context.Statements.AsNoTracking()
                    .Where(s => ids.Contains(s.StatementId))
                    .Select(s => new { s.StatementId, s.Payload })
                    .ToDictionaryAsync(s => s.StatementId, s => s.Payload, cancellationToken)
                    .ConfigureAwait(false);

                var inserted = new List<(StatementRecord Record, StatementIndex Index)>();
                foreach (var record in statements)
                {
                    if (existing.TryGetValue(record.Id, out var payload))
                    {
                        // IEEE 9274.1.1, 4.1.6.1 PUT/POST: no state change for a known id; 409 when it does not match
                        if (!StatementEquivalence.AreEquivalent(ReadStored(payload), record.Statement))
                        {
                            return StatementStoreResult.Conflict(record.Id);
                        }

                        continue;
                    }

                    inserted.Add((record, StatementIndex.Build(record.Statement)));
                }

                if (inserted.Count > 0)
                {
                    AddStatements(context, inserted);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await AddStatementRefsAsync(context, inserted, cancellationToken).ConfigureAwait(false);
                    await ApplyVoidingAsync(context, inserted, cancellationToken).ConfigureAwait(false);
                    await UpdateActivitiesAsync(context, inserted, updateDefinitions, cancellationToken).ConfigureAwait(false);
                    await UpdateActorsAsync(context, inserted, updateDefinitions, cancellationToken).ConfigureAwait(false);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return StatementStoreResult.Stored;
            }
        }
    }

    private static Statement ReadStored(string payload)
    {
        // Stored payloads were validated on the way in; 2.0.0 reading accepts everything 1.0.3 does.
        var result = XapiJson.ParseStatement(payload, XapiVersion.V2_0_0, XapiReadMode.Lenient);
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException("A stored statement could not be read: " + string.Join("; ", result.Errors));
    }

    private static void AddStatements(XapiLrsDbContext context, List<(StatementRecord Record, StatementIndex Index)> inserted)
    {
        foreach (var (record, index) in inserted)
        {
            var statement = record.Statement;
            // Npgsql only writes UTC DateTimeOffset values to timestamptz
            var stored = statement.Stored!.Value.ToUniversalTime();
            context.Statements.Add(new StatementEntity
            {
                Pk = record.Key,
                StatementId = record.Id,
                Stored = stored,
                Timestamp = statement.Timestamp?.ToUniversalTime() ?? stored,
                VerbIri = statement.Verb.Id.Value,
                Registration = statement.Context?.Registration,
                RefStatementId = (statement.Object as StatementRef)?.Id,
                Version = statement.Version ?? "",
                AuthorityKey = record.AuthorityKey,
                Payload = record.Json,
            });

            foreach (var (usage, ifi, type) in index.ActorLinks)
            {
                context.StatementActors.Add(new StatementActorEntity { StatementPk = record.Key, Usage = usage, ActorIfi = ifi, ActorType = type });
            }

            foreach (var (usage, iri) in index.ActivityLinks)
            {
                context.StatementActivities.Add(new StatementActivityEntity { StatementPk = record.Key, Usage = usage, ActivityIri = iri });
            }
        }
    }

    // xapi_statement_ref holds, for each statement whose object is a StatementRef, every statement id it reaches
    // by following StatementRefs (IEEE 9274.1.1, 4.1.6.1 "Filter Conditions for StatementRefs" applies
    // recursively). Targets need not exist yet; when one arrives later, the statements that already reach it
    // inherit what it reaches.
    private static async Task AddStatementRefsAsync(XapiLrsDbContext context, List<(StatementRecord Record, StatementIndex Index)> inserted, CancellationToken cancellationToken)
    {
        foreach (var (record, _) in inserted)
        {
            if (record.Statement.Object is not StatementRef target)
            {
                continue;
            }

            var reached = new HashSet<Guid> { target.Id };
            var targetPk = await context.Statements
                .Where(s => s.StatementId == target.Id)
                .Select(s => (Guid?)s.Pk)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (targetPk is { } pk)
            {
                reached.UnionWith(await context.StatementRefs
                    .Where(r => r.AncestorPk == pk)
                    .Select(r => r.DescendantId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false));
            }

            foreach (var id in reached)
            {
                context.StatementRefs.Add(new StatementRefEntity { AncestorPk = record.Key, DescendantId = id });
            }

            var ancestors = await context.StatementRefs
                .Where(r => r.DescendantId == record.Id)
                .Select(r => r.AncestorPk)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var ancestor in ancestors)
            {
                var known = await context.StatementRefs
                    .Where(r => r.AncestorPk == ancestor)
                    .Select(r => r.DescendantId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var id in reached.Except(known))
                {
                    context.StatementRefs.Add(new StatementRefEntity { AncestorPk = ancestor, DescendantId = id });
                }
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // IEEE 9274.1.1, 4.2.5: a statement is voided if and only if it is not itself a voiding statement and a
    // voiding statement refers to it. The target may arrive before or after the voiding statement, or never.
    private static async Task ApplyVoidingAsync(XapiLrsDbContext context, List<(StatementRecord Record, StatementIndex Index)> inserted, CancellationToken cancellationToken)
    {
        var targets = inserted
            .Where(i => IsVoiding(i.Record.Statement))
            .Select(i => ((StatementRef)i.Record.Statement.Object).Id)
            .Distinct()
            .ToList();
        if (targets.Count > 0)
        {
            await context.Statements
                .Where(s => targets.Contains(s.StatementId) && s.VerbIri != VoidedVerb && !s.IsVoided)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.IsVoided, true), cancellationToken)
                .ConfigureAwait(false);
        }

        var others = inserted
            .Where(i => !IsVoiding(i.Record.Statement))
            .Select(i => i.Record.Id)
            .ToList();
        if (others.Count > 0)
        {
            await context.Statements
                .Where(s => others.Contains(s.StatementId) &&
                            context.Statements.Any(v => v.RefStatementId == s.StatementId && v.VerbIri == VoidedVerb))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.IsVoided, true), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool IsVoiding(Statement statement) => statement.Verb.Id == Verbs.VoidedId;

    private static async Task UpdateActivitiesAsync(XapiLrsDbContext context, List<(StatementRecord Record, StatementIndex Index)> inserted, bool updateDefinitions, CancellationToken cancellationToken)
    {
        var activities = inserted.SelectMany(i => i.Index.Activities).ToList();
        if (activities.Count == 0)
        {
            return;
        }

        var iris = activities.Select(a => a.Id.Value).Distinct().ToList();
        var rows = await context.Activities
            .Where(a => iris.Contains(a.Iri))
            .ToDictionaryAsync(a => a.Iri, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var canonical = new Dictionary<string, Activity>(StringComparer.Ordinal);
        foreach (var activity in activities)
        {
            var iri = activity.Id.Value;
            if (!canonical.TryGetValue(iri, out var current))
            {
                current = rows.TryGetValue(iri, out var row) ? ReadActivity(row.Payload) : new Activity(activity.Id);
            }

            // Without the define scope, ids are recorded but definitions are not (xAPI 1.0.3, Communication 4.2)
            canonical[iri] = updateDefinitions
                ? current with { Definition = DefinitionMerge.Merge(current.Definition, activity.Definition) }
                : current;
        }

        foreach (var (iri, activity) in canonical)
        {
            var payload = XapiJson.SerializeActivity(activity, XapiVersion.V2_0_0);
            if (!rows.TryGetValue(iri, out var row))
            {
                context.Activities.Add(new ActivityEntity { Iri = iri, Payload = payload });
            }
            else if (!string.Equals(row.Payload, payload, StringComparison.Ordinal))
            {
                row.Payload = payload;
            }
        }
    }

    private static Activity ReadActivity(string payload)
    {
        var result = XapiJson.ParseActivity(payload, XapiVersion.V2_0_0, XapiReadMode.Lenient);
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException("A stored activity could not be read: " + string.Join("; ", result.Errors));
    }

    private static Actor ReadActor(string payload)
    {
        var result = XapiJson.ParseActor(payload, XapiVersion.V2_0_0, XapiReadMode.Lenient);
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException("A stored actor could not be read: " + string.Join("; ", result.Errors));
    }

    // One row per identified Agent or Group, holding the latest form seen (name included) for the Agents
    // resource. Anonymous Groups have no identity of their own; their members get rows.
    private static async Task UpdateActorsAsync(XapiLrsDbContext context, List<(StatementRecord Record, StatementIndex Index)> inserted, bool updateDefinitions, CancellationToken cancellationToken)
    {
        var latest = new Dictionary<(string Ifi, short Type), Actor>();
        foreach (var actor in inserted.SelectMany(i => i.Index.Actors))
        {
            var key = actor switch
            {
                Agent agent => (IfiKeys.For(agent.Ifi), ActorType.Agent),
                Group group => (IfiKeys.For(group.Ifi!), ActorType.Group),
                _ => throw new ArgumentOutOfRangeException(nameof(inserted), actor.GetType().Name, "Unknown actor type."),
            };
            latest[key] = updateDefinitions ? actor : actor with { Name = null };
        }

        if (latest.Count == 0)
        {
            return;
        }

        var ifis = latest.Keys.Select(k => k.Ifi).Distinct().ToList();
        var rows = await context.Actors
            .Where(a => ifis.Contains(a.Ifi))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byKey = rows.ToDictionary(r => (r.Ifi, r.Type));

        foreach (var (key, actor) in latest)
        {
            var payload = XapiJson.SerializeActor(actor, XapiVersion.V2_0_0);
            if (!byKey.TryGetValue(key, out var row))
            {
                context.Actors.Add(new ActorEntity { Ifi = key.Ifi, Type = key.Type, Payload = payload });
            }
            else if (updateDefinitions && actor.Name is not null && !string.Equals(row.Payload, payload, StringComparison.Ordinal))
            {
                row.Payload = payload;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Statement write attempt {Attempt} hit a concurrent write; retrying")]
    private static partial void LogRetry(ILogger logger, int attempt, Exception exception);
}
