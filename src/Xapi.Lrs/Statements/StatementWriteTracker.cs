using System.Collections.Concurrent;

namespace Xapi.Lrs.Statements;

// X-Experience-API-Consistent-Through (IEEE 9274.1.1, 4.1.6.1 GET): statements get their stored time before
// the write commits, so a statement stored "earlier" can become visible after a later one. While writes are in
// flight, the header reports the start of the oldest one; otherwise the current time. This covers one process
// only; several LRS instances on one database are a documented limitation.
internal sealed class StatementWriteTracker(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<long, DateTimeOffset> _inFlight = new();
    private long _next;

    public IDisposable Begin()
    {
        var id = Interlocked.Increment(ref _next);
        _inFlight[id] = timeProvider.GetUtcNow();
        return new Registration(this, id);
    }

    public DateTimeOffset ConsistentThrough()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var started in _inFlight.Values)
        {
            if (started < now)
            {
                now = started;
            }
        }

        return now;
    }

    private sealed class Registration(StatementWriteTracker tracker, long id) : IDisposable
    {
        public void Dispose() => tracker._inFlight.TryRemove(id, out _);
    }
}
