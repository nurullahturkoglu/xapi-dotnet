using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Xapi.Lrs.Storage;

internal sealed partial class EfDocumentStore(
    IDbContextFactory<XapiLrsDbContext> contextFactory,
    XapiLrsDatabaseProvider provider,
    TimeProvider time,
    ILogger<EfDocumentStore> logger) : IDocumentStore
{
    private const int MaxAttempts = 8;

    public async Task<StoredDocument?> GetAsync(DocumentScope scope, string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(id);
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var row = await Single(context, scope, id).AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            return row is null ? null : ToStored(row);
        }
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(DocumentScope scope, DateTimeOffset? since, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var rows = await Scoped(context, scope)
                .Select(d => new { d.DocumentId, d.Updated })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // The since filter runs here: SQLite stores DateTimeOffset as text, which EF Core cannot compare in
            // SQL, and a scope holds few documents. A state id can occur once per registration.
            return [.. rows
                .Where(r => since is not { } after || r.Updated > after)
                .GroupBy(r => r.DocumentId, StringComparer.Ordinal)
                .Select(g => new DocumentSummary(g.Key, g.Max(r => r.Updated)))];
        }
    }

    public async Task<StoredDocument?> ChangeAsync(DocumentScope scope, string id, Func<StoredDocument?, DocumentChange> decide, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(decide);

        // A concurrent change of the same document fails this one on the ETag concurrency token, a unique key
        // (two creates) or a serialization check. Running it again reads the other change, so decide sees
        // the document as it is now and a stale If-Match fails with 412 instead of overwriting.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TryChangeAsync(scope, id, decide, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < MaxAttempts && (exception is DbUpdateConcurrencyException || provider.IsTransientWriteFailure(exception)))
            {
                LogRetry(logger, attempt, exception);
                var delay = TimeSpan.FromMilliseconds((5 << attempt) + Random.Shared.Next(0, 10 << attempt));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task DeleteAllAsync(DocumentScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var deleting = scope.Resource switch
            {
                DocumentResource.State => States(context, scope).ExecuteDeleteAsync(cancellationToken),
                DocumentResource.ActivityProfile => ActivityProfiles(context, scope).ExecuteDeleteAsync(cancellationToken),
                DocumentResource.AgentProfile => AgentProfiles(context, scope).ExecuteDeleteAsync(cancellationToken),
                _ => throw UnknownResource(scope),
            };
            await deleting.ConfigureAwait(false);
        }
    }

    private async Task<StoredDocument?> TryChangeAsync(DocumentScope scope, string id, Func<StoredDocument?, DocumentChange> decide, CancellationToken cancellationToken)
    {
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var transaction = await provider.BeginWriteTransactionAsync(context, cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var row = await Single(context, scope, id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                var current = row is null ? null : ToStored(row);
                var change = decide(current);
                switch (change.Kind)
                {
                    case DocumentChangeKind.Store:
                        if (row is null)
                        {
                            row = NewRow(scope, id);
                            context.Add(row);
                        }

                        row.Content = change.Content.ToArray();
                        row.ContentType = change.ContentType!;
                        row.ETag = ETagOf(row.Content);
                        row.Updated = Now();
                        break;
                    case DocumentChangeKind.Delete when row is not null:
                        context.Remove(row);
                        row = null;
                        break;
                    default:
                        return current;
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return row is null ? null : ToStored(row);
            }
        }
    }

    // xAPI 1.0.3 requires the ETag to be the SHA-1 of the content in hex (Communication 3.1, LRS Requirements);
    // 2.0 leaves the value open, so one rule serves both versions. SHA-1 identifies content here, it is not
    // used for security.
#pragma warning disable CA5350
    internal static string ETagOf(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA1.HashData(content));
#pragma warning restore CA5350

    // Truncated to microseconds, the precision of PostgreSQL timestamptz, so the value returned after a write
    // equals the value read back later.
    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return new DateTimeOffset(now.Ticks - (now.Ticks % 10), TimeSpan.Zero);
    }

    private static StoredDocument ToStored(DocumentEntity row) =>
        new(row.DocumentId, row.Content, row.ContentType, row.ETag, row.Updated);

    private static DocumentEntity NewRow(DocumentScope scope, string id) => scope.Resource switch
    {
        DocumentResource.State => new StateDocumentEntity
        {
            ActivityIri = ActivityIri(scope),
            AgentIfi = AgentIfi(scope),
            Registration = scope.Registration ?? Guid.Empty,
            DocumentId = id,
        },
        DocumentResource.ActivityProfile => new ActivityProfileEntity { ActivityIri = ActivityIri(scope), DocumentId = id },
        DocumentResource.AgentProfile => new AgentProfileEntity { AgentIfi = AgentIfi(scope), DocumentId = id },
        _ => throw UnknownResource(scope),
    };

    private static IQueryable<DocumentEntity> Single(XapiLrsDbContext context, DocumentScope scope, string id)
    {
        switch (scope.Resource)
        {
            case DocumentResource.State:
                var registration = scope.Registration ?? Guid.Empty;
                return States(context, scope).Where(d => d.Registration == registration && d.DocumentId == id);
            case DocumentResource.ActivityProfile:
                return ActivityProfiles(context, scope).Where(d => d.DocumentId == id);
            case DocumentResource.AgentProfile:
                return AgentProfiles(context, scope).Where(d => d.DocumentId == id);
            default:
                throw UnknownResource(scope);
        }
    }

    private static IQueryable<DocumentEntity> Scoped(XapiLrsDbContext context, DocumentScope scope) => scope.Resource switch
    {
        DocumentResource.State => States(context, scope),
        DocumentResource.ActivityProfile => ActivityProfiles(context, scope),
        DocumentResource.AgentProfile => AgentProfiles(context, scope),
        _ => throw UnknownResource(scope),
    };

    // Without a registration, the scope covers every registration (see DocumentScope.Registration)
    private static IQueryable<StateDocumentEntity> States(XapiLrsDbContext context, DocumentScope scope)
    {
        var iri = ActivityIri(scope);
        var ifi = AgentIfi(scope);
        var states = context.StateDocuments.Where(d => d.ActivityIri == iri && d.AgentIfi == ifi);
        return scope.Registration is { } registration ? states.Where(d => d.Registration == registration) : states;
    }

    private static IQueryable<ActivityProfileEntity> ActivityProfiles(XapiLrsDbContext context, DocumentScope scope)
    {
        var iri = ActivityIri(scope);
        return context.ActivityProfiles.Where(d => d.ActivityIri == iri);
    }

    private static IQueryable<AgentProfileEntity> AgentProfiles(XapiLrsDbContext context, DocumentScope scope)
    {
        var ifi = AgentIfi(scope);
        return context.AgentProfiles.Where(d => d.AgentIfi == ifi);
    }

    private static string ActivityIri(DocumentScope scope) =>
        scope.ActivityId?.Value ?? throw new ArgumentException("The scope has no Activity.", nameof(scope));

    // Documents of the same Agent are found whatever else (name, objectType) the agent parameter carries
    private static string AgentIfi(DocumentScope scope) =>
        scope.Agent is { } agent ? IfiKeys.For(agent.Ifi) : throw new ArgumentException("The scope has no Agent.", nameof(scope));

    private static ArgumentOutOfRangeException UnknownResource(DocumentScope scope) =>
        new(nameof(scope), scope.Resource, "Unknown document resource.");

    [LoggerMessage(Level = LogLevel.Debug, Message = "Document write attempt {Attempt} hit a concurrent write; retrying")]
    private static partial void LogRetry(ILogger logger, int attempt, Exception exception);
}
