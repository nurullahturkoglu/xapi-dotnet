using System.Linq.Expressions;
using System.Text;
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

    // On SQLite, how far the xapi_statement_ref rows of a query are counted to choose where its StatementRef matches
    // start (StatementRefMatchesAsync). Counting reads an index only, about 0.3 ms for 10,000 rows.
    private const int MaxStatementRefsCounted = 10_000;

    // DatabaseFacade.ProviderName of Microsoft.EntityFrameworkCore.Sqlite, which this assembly does not reference
    private const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";

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
                : new StoredStatement(row.StatementId, row.Stored, row.Version, row.IsVoided, row.AuthorityKey, Utf8(row.Payload));
        }
    }

    public async Task<StatementPage> QueryAsync(StatementFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(filter.Limit, 1);
        Guid? after = null;
        if (filter.ContinuationToken is { } token)
        {
            after = StatementKeys.TryReadContinuationToken(token, out var key)
                ? key
                : throw new ArgumentException("The continuation token is not one this store returned.", nameof(filter));
        }

        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            // One more than the page, to tell whether another page follows
            var take = filter.Limit + 1;

            // Time and paging bounds apply to the returned statement, not to the statements it targets
            var bounds = KeyBounds(filter, after);

            // IEEE 9274.1.1, 4.1.6.1 "Voided Statements": never returned by a query
            var visible = Within(context.Statements.AsNoTracking().Where(s => !s.IsVoided), s => s.Pk, bounds);
            if (filter.AuthorityKey is { } authority)
            {
                visible = visible.Where(s => s.AuthorityKey == authority);
            }

            var query = visible;
            var conditions = Conditions(context, filter, bounds);
            if (conditions.Count > 0)
            {
                var candidates = await CandidatesAsync(context, filter, visible, conditions, bounds, take, cancellationToken).ConfigureAwait(false);
                query = context.Statements.AsNoTracking().Where(s => candidates.Contains(s.Pk));
            }

            query = filter.Ascending ? query.OrderBy(s => s.Pk) : query.OrderByDescending(s => s.Pk);
            var rows = await query
                .Take(take)
                .Select(s => new { s.Pk, s.StatementId, s.Stored, s.Version, s.IsVoided, s.AuthorityKey, s.Payload })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var more = rows.Count > filter.Limit;
            if (more)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            var statements = rows.ConvertAll(r => new StoredStatement(r.StatementId, r.Stored, r.Version, r.IsVoided, r.AuthorityKey, Utf8(r.Payload)));
            return new StatementPage(statements, more ? StatementKeys.ToContinuationToken(rows[^1].Pk) : null);
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

    public async Task<IReadOnlyList<AttachmentSummary>> GetAttachmentSummariesAsync(IReadOnlyCollection<Guid> statementIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statementIds);
        if (statementIds.Count == 0)
        {
            return [];
        }

        var ids = statementIds.Distinct().ToList();
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var sha2s = context.StatementAttachments
                .Where(a => context.Statements.Any(s => s.Pk == a.StatementPk && ids.Contains(s.StatementId)))
                .Select(a => a.Sha2);
            var rows = await context.AttachmentContents.AsNoTracking()
                .Where(c => sha2s.Contains(c.Sha2))
                .OrderBy(c => c.Sha2)
                .Select(c => new { c.Sha2, c.ContentType, c.Length })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return rows.ConvertAll(r => new AttachmentSummary(r.Sha2, r.ContentType, r.Length));
        }
    }

    public async Task<AttachmentContent?> GetAttachmentContentAsync(string sha2, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sha2);
        var key = sha2.ToLowerInvariant();
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var row = await context.AttachmentContents.AsNoTracking()
                .Where(c => c.Sha2 == key)
                .Select(c => new { c.Sha2, c.ContentType, c.Content })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return row is null ? null : new AttachmentContent(row.Sha2, row.ContentType, row.Content);
        }
    }

    // The keys of the statements a filtered query may return, at most `take` from each index range. Each range is
    // read in key order and stops once it has a page, so a filter costs about a page of index entries whether it
    // matches most statements or none. One condition such as "verb = x OR the StatementRef target has verb x"
    // would instead make the database walk every statement in key order and test it.
    private static async Task<IQueryable<Guid>> CandidatesAsync(
        XapiLrsDbContext context,
        StatementFilter filter,
        IQueryable<StatementEntity> visible,
        List<Condition> conditions,
        List<Expression<Func<Guid, bool>>> bounds,
        int take,
        CancellationToken cancellationToken)
    {
        // The first condition is read from its index; the others are tested on the statements it finds
        var driver = conditions[0];
        var rows = conditions.Skip(1).Aggregate(visible, (query, condition) => query.Where(condition.Match));
        var ranges = driver.Ranges(rows).Select(keys => First(keys, filter.Ascending, take * driver.RowsPerStatement)).ToList();

        if (await StatementRefMatchesAsync(context, filter, conditions, bounds, cancellationToken).ConfigureAwait(false) is { } ancestors)
        {
            ranges.Add(First(visible.Where(s => ancestors.Contains(s.Pk)).Select(s => s.Pk), filter.Ascending, take));
        }

        return ranges.Aggregate(Queryable.Concat);
    }

    // 4.1.6.1 "Filter Conditions for StatementRefs": a statement also matches when a statement it targets matches,
    // recursively (xapi_statement_ref holds every id reached), and voided targets count (4.1.6.1 "Voided
    // Statements": statements targeting a voided statement are returned). The keys of those statements within the
    // bounds, or null when there cannot be any.
    //
    // Unlike the other ranges this one cannot stop after a page, because targets are not in the order of the
    // statements that reach them. It can start from either side: from the xapi_statement_ref rows within the
    // bounds, testing the target of each, or from the statements that match the filters, read from their indexes,
    // looking up the rows that reach each (ix_xapi_statement_ref_descendant_id). Each costs a lookup per row it
    // starts from, so the smaller side should go first: without StatementRefs or for a verb most statements have,
    // the rows; for a learner, a registration or a verb no statement has, the matches. PostgreSQL chooses from its
    // statistics. SQLite has none unless ANALYZE has run and always starts from the rows, so there the rows are
    // counted first (up to MaxStatementRefsCounted), then the matches up to that number, and the smaller side goes
    // first. The counts only choose the plan; both plans return the same keys.
    private static async Task<IQueryable<Guid>?> StatementRefMatchesAsync(
        XapiLrsDbContext context,
        StatementFilter filter,
        List<Condition> conditions,
        List<Expression<Func<Guid, bool>>> bounds,
        CancellationToken cancellationToken)
    {
        var references = Within(context.StatementRefs, r => r.AncestorPk, bounds);
        var targets = context.Statements.Where(conditions.Select(c => c.Match).Aggregate(And));
        var fromReferences = references
            .Where(r => targets.Any(t => t.StatementId == r.DescendantId))
            .Select(r => r.AncestorPk);
        if (context.Database.ProviderName != SqliteProviderName)
        {
            return fromReferences;
        }

        // The counts and the page share one connection
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var referenceCount = await First(references.Select(r => r.AncestorPk), filter.Ascending, MaxStatementRefsCounted)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        if (referenceCount == 0)
        {
            return null;
        }

        // The bounds apply to the statement returned, not to its target
        var targetConditions = Conditions(context, filter, []);
        var targetRows = targetConditions.Skip(1).Aggregate(context.Statements.AsQueryable(), (query, condition) => query.Where(condition.Match));
        var targetRanges = targetConditions[0].Ranges(targetRows).ToList();
        var targetCount = await targetRanges.Select(keys => First(keys, filter.Ascending, referenceCount)).Aggregate(Queryable.Concat)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        if (targetCount == 0)
        {
            return null;
        }

        if (targetCount >= referenceCount)
        {
            return fromReferences;
        }

        return targetRanges.Aggregate(Queryable.Concat)
            .Join(context.Statements, k => k, t => t.Pk, (_, t) => t.StatementId)
            .Join(references, id => id, r => r.DescendantId, (_, r) => r.AncestorPk);
    }

    private static IQueryable<Guid> First(IQueryable<Guid> keys, bool ascending, int take) =>
        (ascending ? keys.OrderBy(k => k) : keys.OrderByDescending(k => k)).Take(take);

    // A content filter, as a condition on one statement row and as the index ranges that together hold the keys of
    // every statement it matches, each limited to the given rows. A range may hold a statement's key up to
    // RowsPerStatement times; the IN of the outer query removes the repeats. (DISTINCT inside a range would make
    // SQLite read and sort the whole range before the LIMIT.)
    private sealed record Condition(
        Expression<Func<StatementEntity, bool>> Match,
        Func<IQueryable<StatementEntity>, IEnumerable<IQueryable<Guid>>> Ranges,
        int RowsPerStatement = 1);

    // The content filters, the one to read from its index first. In typical data a registration, an activity or a
    // learner matches fewer statements than a verb, and one range is cheaper than one per related_* usage. Any
    // order returns the same statements.
    private static List<Condition> Conditions(XapiLrsDbContext context, StatementFilter filter, List<Expression<Func<Guid, bool>>> bounds)
    {
        var conditions = new List<(int Rank, Condition Condition)>();
        if (filter.Registration is { } registration)
        {
            conditions.Add((0, new(
                t => t.Registration == registration,
                rows => [rows.Where(s => s.Registration == registration).Select(s => s.Pk)])));
        }

        if (filter.Activity is { } activity)
        {
            var iri = activity.Value;
            var usages = filter.RelatedActivities ? ActivityUsage.All : ActivityUsage.ObjectOnly;
            conditions.Add((filter.RelatedActivities ? 4 : 1, new(
                // The usages spelled out, so the database can look up (iri, usage, statement) instead of reading
                // every row of the activity
                t => context.StatementActivities.Any(a => a.StatementPk == t.Pk && a.ActivityIri == iri && EF.Constant(usages).Contains(a.Usage)),
                rows => usages.Select(usage =>
                    Within(context.StatementActivities.Where(a => a.ActivityIri == iri && a.Usage == usage), a => a.StatementPk, bounds)
                        .Join(rows, a => a.StatementPk, s => s.Pk, (a, _) => a.StatementPk)))));
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
            var usages = filter.RelatedAgents ? ActorUsage.All : ActorUsage.ActorOrObject;
            conditions.Add((filter.RelatedAgents ? 5 : 2, new(
                t => context.StatementActors.Any(a => a.StatementPk == t.Pk && a.ActorIfi == ifi && EF.Constant(usages).Contains(a.Usage)),
                rows => usages.Select(usage =>
                    Within(context.StatementActors.Where(a => a.ActorIfi == ifi && a.Usage == usage), a => a.StatementPk, bounds)
                        .Join(rows, a => a.StatementPk, s => s.Pk, (a, _) => a.StatementPk)),
                // An Agent and a Group with the same identifier in one usage are two rows (the key includes the type)
                RowsPerStatement: 2)));
        }

        if (filter.Verb is { } verb)
        {
            var iri = verb.Value;
            conditions.Add((3, new(
                t => t.VerbIri == iri,
                rows => [rows.Where(s => s.VerbIri == iri).Select(s => s.Pk)])));
        }

        return [.. conditions.OrderBy(c => c.Rank).Select(c => c.Condition)];
    }

    // since, until and the previous page, as conditions on a storage key
    private static List<Expression<Func<Guid, bool>>> KeyBounds(StatementFilter filter, Guid? after)
    {
        var bounds = new List<Expression<Func<Guid, bool>>>();
        if (filter.Since is { } since)
        {
            var first = StatementKeys.FirstAfter(since);
            bounds.Add(k => k.CompareTo(first) >= 0);
        }

        if (filter.Until is { } until)
        {
            var end = StatementKeys.FirstAfter(until);
            bounds.Add(k => k.CompareTo(end) < 0);
        }

        if (after is { } last)
        {
            bounds.Add(filter.Ascending ? k => k.CompareTo(last) > 0 : k => k.CompareTo(last) < 0);
        }

        return bounds;
    }

    // The bounds on the key column of a table, so that an index range of that table starts and ends at them
    private static IQueryable<T> Within<T>(IQueryable<T> query, Expression<Func<T, Guid>> key, List<Expression<Func<Guid, bool>>> bounds) =>
        bounds.Aggregate(query, (current, bound) =>
        {
            var row = Expression.Parameter(typeof(T), "r");
            var keyOfRow = new ParameterReplacer(key.Parameters[0], row).Visit(key.Body);
            return current.Where(Expression.Lambda<Func<T, bool>>(new ParameterReplacer(bound.Parameters[0], keyOfRow).Visit(bound.Body), row));
        });

    private static Expression<Func<StatementEntity, bool>> And(Expression<Func<StatementEntity, bool>> left, Expression<Func<StatementEntity, bool>> right)
    {
        var parameter = Expression.Parameter(typeof(StatementEntity), "t");
        var leftBody = new ParameterReplacer(left.Parameters[0], parameter).Visit(left.Body);
        var rightBody = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<StatementEntity, bool>>(Expression.AndAlso(leftBody, rightBody), parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
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
                    await AddAttachmentsAsync(context, inserted, cancellationToken).ConfigureAwait(false);
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

    // The payload column is TEXT (ADR-0011), which both providers read and write as a string, so the statement
    // JSON is converted between UTF-8 and a string once, here, on its way into and out of the database
    private static ReadOnlyMemory<byte> Utf8(string payload) => Encoding.UTF8.GetBytes(payload);

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
                Payload = Encoding.UTF8.GetString(record.Utf8Json.Span),
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

    // Attachment data is stored once per SHA-2 (ADR-0011), whether it arrived in this batch or an earlier one
    private static async Task AddAttachmentsAsync(XapiLrsDbContext context, List<(StatementRecord Record, StatementIndex Index)> inserted, CancellationToken cancellationToken)
    {
        var contents = inserted
            .SelectMany(i => i.Record.Attachments)
            .DistinctBy(a => a.Sha2, StringComparer.Ordinal)
            .ToList();
        if (contents.Count == 0)
        {
            return;
        }

        var sha2s = contents.ConvertAll(c => c.Sha2);
        var known = await context.AttachmentContents
            .Where(c => sha2s.Contains(c.Sha2))
            .Select(c => c.Sha2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var content in contents.Where(c => !known.Contains(c.Sha2, StringComparer.Ordinal)))
        {
            context.AttachmentContents.Add(new AttachmentContentEntity
            {
                Sha2 = content.Sha2,
                ContentType = content.ContentType,
                Length = content.Content.Length,
                Content = content.Content.ToArray(),
            });
        }

        foreach (var (record, _) in inserted)
        {
            foreach (var sha2 in record.Attachments.Select(a => a.Sha2).Distinct(StringComparer.Ordinal))
            {
                context.StatementAttachments.Add(new StatementAttachmentEntity { StatementPk = record.Key, Sha2 = sha2 });
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
