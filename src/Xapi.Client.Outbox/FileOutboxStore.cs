using System.Globalization;
using Xapi.Client.Outbox.Internal;

namespace Xapi.Client.Outbox;

/// <summary>
/// An <see cref="OutboxStore"/> that keeps the outbox in a directory, as an append-only journal that is flushed
/// to disk (<c>fsync</c>) before each change is acknowledged.
/// </summary>
/// <remarks>
/// <para>
/// The directory holds <c>outbox.v1.jsonl</c>, the journal: one JSON object per line, each with a CRC-32 of its
/// record, and <c>outbox.lock</c>, held open while the store is open so that a second store (in this or another
/// process) cannot use the same directory. When most of the journal describes Statements that are gone, it is
/// compacted: the live state is written to <c>outbox.v1.jsonl.tmp</c>, flushed, and renamed over the journal.
/// </para>
/// <para>
/// If the process dies in the middle of a write, the journal ends with an incomplete line; the next open cuts it
/// off. That write was never acknowledged, so nothing reported as stored is lost. A rename interrupted by a crash
/// leaves either the old or the new journal, each complete. Lines damaged on disk are skipped, and a copy of the
/// damaged file is kept; see <see cref="Recovery"/>.
/// </para>
/// <para>
/// The whole queue is also kept in memory, and the journal is read once, by <see cref="OpenAsync"/>. The store
/// suits queues of up to tens of thousands of Statements. All methods are thread-safe; they complete
/// synchronously, blocking on the disk flush.
/// </para>
/// </remarks>
public sealed class FileOutboxStore : OutboxStore, IDisposable
{
    private const string JournalFileName = "outbox.v1.jsonl";
    private const string LockFileName = "outbox.lock";
    private const long DefaultCompactionThreshold = 256 * 1024;

#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private readonly OutboxQueue _queue = new();
    private readonly string _journalPath;
    private readonly long _compactionThreshold;
    private readonly FileStream _lock;

    // Bytes each live entry takes in a compacted journal, to decide when compaction pays off
    private Dictionary<Guid, int> _pendingBytes = [];
    private Dictionary<Guid, int> _deadBytes = [];
    private long _liveBytes;

    private FileStream _journal = null!;
    private long _length;
    private bool _broken;
    private bool _disposed;

    // Takes the directory lock; the journal is read by the caller
    private FileOutboxStore(string directory, long compactionThreshold)
    {
        Directory = Path.GetFullPath(directory);
        _compactionThreshold = compactionThreshold;
        _journalPath = Path.Combine(Directory, JournalFileName);
        System.IO.Directory.CreateDirectory(Directory);

        var lockPath = Path.Combine(Directory, LockFileName);
        try
        {
            _lock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new IOException($"The outbox directory {Directory} is in use by another FileOutboxStore ({lockPath} is locked).", exception);
        }

        try
        {
            // A leftover temporary file is an interrupted compaction; the journal it was meant to replace is complete
            File.Delete(_journalPath + ".tmp");
        }
        catch
        {
            _lock.Dispose();
            throw;
        }
    }

    /// <summary>The full path of the directory.</summary>
    public string Directory { get; }

    /// <summary>
    /// What had to be repaired when the journal was opened, or <see langword="null"/> when it was intact.
    /// </summary>
    public FileOutboxRecovery? Recovery { get; private set; }

    /// <summary>Opens the store in a directory, creating the directory if needed, and reads the journal.</summary>
    /// <param name="directory">The directory. It should be used by this store only.</param>
    /// <param name="cancellationToken">Cancels reading the journal.</param>
    /// <returns>The open store. Dispose it to release the directory.</returns>
    /// <exception cref="IOException">Another <see cref="FileOutboxStore"/> has the directory open.</exception>
    public static Task<FileOutboxStore> OpenAsync(string directory, CancellationToken cancellationToken = default) =>
        OpenCoreAsync(Guard.NotNull(directory), DefaultCompactionThreshold, cancellationToken);

    internal static async Task<FileOutboxStore> OpenCoreAsync(string directory, long compactionThreshold, CancellationToken cancellationToken)
    {
        var store = new FileOutboxStore(directory, compactionThreshold);
        try
        {
            store.Load(await ReadJournalAsync(store._journalPath, cancellationToken).ConfigureAwait(false));
            return store;
        }
        catch
        {
            store._lock.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens the store with blocking reads, for dependency injection, whose factories cannot be asynchronous.
    /// </summary>
    internal static FileOutboxStore Open(string directory)
    {
        var store = new FileOutboxStore(Guard.NotNull(directory), DefaultCompactionThreshold);
        try
        {
            store.Load(File.Exists(store._journalPath) ? File.ReadAllBytes(store._journalPath) : []);
            return store;
        }
        catch
        {
            store._lock.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public override ValueTask<bool> AddAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        Guard.NotNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfUnusable();
            if (_queue.IsPending(entry.StatementId))
            {
                return new(false);
            }

            Commit(new AddRecord(entry));
            return new(true);
        }
    }

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<OutboxEntry>> GetPendingAsync(int maxCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposed();
            return new(_queue.Peek(maxCount));
        }
    }

    /// <inheritdoc/>
    public override ValueTask<int> GetPendingCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposed();
            return new(_queue.PendingCount);
        }
    }

    /// <inheritdoc/>
    public override ValueTask RemoveAsync(IReadOnlyCollection<Guid> statementIds, CancellationToken cancellationToken)
    {
        Guard.NotNull(statementIds);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfUnusable();
            var ids = statementIds.Where(_queue.IsPending).Distinct().ToList();
            if (ids.Count > 0)
            {
                Commit(new RemoveRecord(ids));
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public override ValueTask RecordFailedAttemptAsync(IReadOnlyCollection<Guid> statementIds, string error, CancellationToken cancellationToken)
    {
        Guard.NotNull(statementIds);
        Guard.NotNull(error);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfUnusable();
            var ids = statementIds.Where(_queue.IsPending).Distinct().ToList();
            if (ids.Count > 0)
            {
                Commit(new AttemptRecord(ids, error));
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public override ValueTask MoveToDeadLettersAsync(OutboxDeadLetter deadLetter, CancellationToken cancellationToken)
    {
        Guard.NotNull(deadLetter);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfUnusable();
            Commit(new DeadLetterRecord(deadLetter));
        }

        return default;
    }

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<OutboxDeadLetter>> GetDeadLettersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposed();
            return new(_queue.DeadLetters.ToList());
        }
    }

    /// <inheritdoc/>
    public override ValueTask<int> GetDeadLetterCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfDisposed();
            return new(_queue.DeadLetterCount);
        }
    }

    /// <inheritdoc/>
    public override ValueTask<bool> RequeueAsync(Guid statementId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfUnusable();
            if (!_queue.IsDeadLetter(statementId))
            {
                return new(false);
            }

            Commit(new RequeueRecord(statementId));
            return new(true);
        }
    }

    /// <inheritdoc/>
    public override ValueTask<bool> RemoveDeadLetterAsync(Guid statementId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfUnusable();
            if (!_queue.IsDeadLetter(statementId))
            {
                return new(false);
            }

            Commit(new RemoveDeadLetterRecord(statementId));
            return new(true);
        }
    }

    /// <summary>Closes the journal and releases the directory lock.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _journal.Dispose();
            _lock.Dispose();
        }
    }

    private static async Task<byte[]> ReadJournalAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        // The directory lock keeps writers out, so the length cannot change while it is read
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var data = new byte[stream.Length];
        var read = 0;
        while (read < data.Length)
        {
#if NET
            var count = await stream.ReadAsync(data.AsMemory(read), cancellationToken).ConfigureAwait(false);
#else
            var count = await stream.ReadAsync(data, read, data.Length - read, cancellationToken).ConfigureAwait(false);
#endif
            if (count == 0)
            {
                throw new EndOfStreamException($"{path} ended after {read} of {data.Length} bytes.");
            }

            read += count;
        }

        return data;
    }

    private void Load(byte[] data)
    {
        var unreadable = 0;
        var position = 0;
        while (position < data.Length)
        {
            var end = Array.IndexOf(data, (byte)'\n', position);
            if (end < 0)
            {
                break;
            }

            var length = end - position;
            if (length > 0)
            {
                if (Journal.Decode(new ReadOnlySpan<byte>(data, position, length)) is { } record)
                {
                    Apply(record, length + 1);
                }
                else
                {
                    unreadable++;
                }
            }

            position = end + 1;
        }

        var tail = data.Length - position;
        string? copyPath = null;
        if (unreadable > 0)
        {
            copyPath = $"{_journalPath}.corrupt-{DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture)}";
            File.WriteAllBytes(copyPath, data);
        }

        var journal = OpenJournal();
        if (tail > 0)
        {
            journal.SetLength(position);
            journal.Flush(flushToDisk: true);
        }

        journal.Seek(0, SeekOrigin.End);
        _length = journal.Length;
        _journal = journal;
        if (unreadable > 0 || tail > 0)
        {
            Recovery = new FileOutboxRecovery(unreadable, tail, copyPath);
        }

        if (unreadable > 0)
        {
            TryCompact();
        }
    }

    private FileStream OpenJournal() =>
        // FileShare.Read lets people inspect the journal (on Windows the reader must allow writing too); the lock
        // file keeps other writers out
        new(_journalPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

    private void Commit(JournalRecord record)
    {
        var line = Journal.Encode(record);
        try
        {
            _journal.Write(line, 0, line.Length);
            _journal.Flush(flushToDisk: true);
        }
        catch
        {
            // Cut off what may have been written, so the next record does not follow a partial line
            try
            {
                _journal.SetLength(_length);
                _journal.Seek(0, SeekOrigin.End);
            }
            catch (IOException)
            {
                _broken = true;
            }

            throw;
        }

        _length += line.Length;
        Apply(record, line.Length);
        if (_length > _compactionThreshold && _length > 2 * _liveBytes)
        {
            TryCompact();
        }
    }

    private void Apply(JournalRecord record, int size)
    {
        switch (record)
        {
            case AddRecord add:
                if (_queue.TryAdd(add.Entry))
                {
                    TrackPending(add.Entry.StatementId, size);
                }

                break;
            case RemoveRecord remove:
                foreach (var id in remove.Ids)
                {
                    if (_queue.Remove(id))
                    {
                        Untrack(_pendingBytes, id);
                    }
                }

                break;
            case AttemptRecord attempt:
                foreach (var id in attempt.Ids)
                {
                    _queue.RecordFailedAttempt(id, attempt.Error);
                }

                break;
            case DeadLetterRecord dead:
                var deadId = dead.DeadLetter.StatementId;
                Untrack(_pendingBytes, deadId);
                Untrack(_deadBytes, deadId);
                _queue.MoveToDeadLetters(dead.DeadLetter);
                _deadBytes[deadId] = size;
                _liveBytes += size;
                break;
            case RequeueRecord requeue:
                var wasPending = _queue.IsPending(requeue.Id);
                var deadSize = _deadBytes.TryGetValue(requeue.Id, out var bytes) ? bytes : 0;
                if (_queue.Requeue(requeue.Id))
                {
                    Untrack(_deadBytes, requeue.Id);
                    if (!wasPending)
                    {
                        // Close enough: an add record of the same Statement is a little shorter than its dead letter
                        TrackPending(requeue.Id, deadSize);
                    }
                }

                break;
            case RemoveDeadLetterRecord removeDead:
                if (_queue.RemoveDeadLetter(removeDead.Id))
                {
                    Untrack(_deadBytes, removeDead.Id);
                }

                break;
        }
    }

    private void TrackPending(Guid id, int size)
    {
        _pendingBytes[id] = size;
        _liveBytes += size;
    }

    private void Untrack(Dictionary<Guid, int> sizes, Guid id)
    {
        if (sizes.TryGetValue(id, out var size))
        {
            sizes.Remove(id);
            _liveBytes -= size;
        }
    }

    /// <summary>Rewrites the journal with only the live state. A failure leaves the old journal in use.</summary>
    private void TryCompact()
    {
        var temporaryPath = _journalPath + ".tmp";
        var pendingBytes = new Dictionary<Guid, int>(_pendingBytes.Count);
        var deadBytes = new Dictionary<Guid, int>(_deadBytes.Count);
        long length = 0;
        try
        {
            using (var temporary = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                foreach (var entry in _queue.Pending)
                {
                    length += Write(temporary, new AddRecord(entry), entry.StatementId, pendingBytes);
                }

                foreach (var deadLetter in _queue.DeadLetters)
                {
                    length += Write(temporary, new DeadLetterRecord(deadLetter), deadLetter.StatementId, deadBytes);
                }

                temporary.Flush(flushToDisk: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            DeferCompaction();
            return;
        }

        // Windows cannot replace a file that is open, so the journal is closed around the rename
        _journal.Dispose();
        var replaced = false;
        try
        {
            File.Replace(temporaryPath, _journalPath, null);
            replaced = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            TryDelete(temporaryPath);
        }

        try
        {
            _journal = OpenJournal();
            _journal.Seek(0, SeekOrigin.End);
            _length = _journal.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The change that triggered compaction is durable; later writes fail until the store is reopened
            _broken = true;
            return;
        }

        if (replaced)
        {
            _pendingBytes = pendingBytes;
            _deadBytes = deadBytes;
            _liveBytes = length;
        }
        else
        {
            DeferCompaction();
        }

        static int Write(FileStream stream, JournalRecord record, Guid id, Dictionary<Guid, int> sizes)
        {
            var line = Journal.Encode(record);
            stream.Write(line, 0, line.Length);
            sizes[id] = line.Length;
            return line.Length;
        }
    }

    // After a failed compaction, wait until the journal has doubled before trying again
    private void DeferCompaction() => _liveBytes = Math.Max(_liveBytes, _length);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Load deletes leftovers too
        }
    }

    private void ThrowIfDisposed()
    {
#if NET
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileOutboxStore));
        }
#endif
    }

    private void ThrowIfUnusable()
    {
        ThrowIfDisposed();
        if (_broken)
        {
            throw new IOException($"A write to {_journalPath} failed and the journal could not be repaired. Open a new FileOutboxStore on the directory.");
        }
    }
}
