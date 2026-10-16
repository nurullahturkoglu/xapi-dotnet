namespace Xapi.Client.Outbox.Internal;

/// <summary>
/// The in-memory state both built-in stores share: pending entries in queue order and dead letters in the order
/// they arrived, each keyed by Statement id. Not thread-safe; the stores lock around it.
/// </summary>
internal sealed class OutboxQueue
{
    private readonly LinkedList<OutboxEntry> _pending = new();
    private readonly Dictionary<Guid, LinkedListNode<OutboxEntry>> _pendingById = [];
    private readonly LinkedList<OutboxDeadLetter> _dead = new();
    private readonly Dictionary<Guid, LinkedListNode<OutboxDeadLetter>> _deadById = [];

    public int PendingCount => _pending.Count;

    public int DeadLetterCount => _dead.Count;

    public IEnumerable<OutboxEntry> Pending => _pending;

    public IEnumerable<OutboxDeadLetter> DeadLetters => _dead;

    public bool IsPending(Guid id) => _pendingById.ContainsKey(id);

    public bool IsDeadLetter(Guid id) => _deadById.ContainsKey(id);

    public bool TryAdd(OutboxEntry entry)
    {
        if (_pendingById.ContainsKey(entry.StatementId))
        {
            return false;
        }

        _pendingById.Add(entry.StatementId, _pending.AddLast(entry));
        return true;
    }

    public List<OutboxEntry> Peek(int maxCount)
    {
        var result = new List<OutboxEntry>(Math.Min(maxCount, _pending.Count));
        for (var node = _pending.First; node is not null && result.Count < maxCount; node = node.Next)
        {
            result.Add(node.Value);
        }

        return result;
    }

    public bool Remove(Guid id)
    {
        if (!_pendingById.TryGetValue(id, out var node))
        {
            return false;
        }

        _pending.Remove(node);
        _pendingById.Remove(id);
        return true;
    }

    public void RecordFailedAttempt(Guid id, string error)
    {
        if (_pendingById.TryGetValue(id, out var node))
        {
            node.Value = node.Value with { Attempts = node.Value.Attempts + 1, LastError = error };
        }
    }

    public void MoveToDeadLetters(OutboxDeadLetter deadLetter)
    {
        Remove(deadLetter.StatementId);
        RemoveDeadLetter(deadLetter.StatementId);
        _deadById.Add(deadLetter.StatementId, _dead.AddLast(deadLetter));
    }

    public bool Requeue(Guid id)
    {
        if (!_deadById.TryGetValue(id, out var node))
        {
            return false;
        }

        var deadLetter = node.Value;
        RemoveDeadLetter(id);
        TryAdd(new OutboxEntry(deadLetter.Statement, deadLetter.EnqueuedAt));
        return true;
    }

    public bool RemoveDeadLetter(Guid id)
    {
        if (!_deadById.TryGetValue(id, out var node))
        {
            return false;
        }

        _dead.Remove(node);
        _deadById.Remove(id);
        return true;
    }
}
