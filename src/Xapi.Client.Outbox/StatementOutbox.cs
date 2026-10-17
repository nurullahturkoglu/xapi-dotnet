using Xapi.Client.Outbox.Internal;
using Xapi.Validation;

namespace Xapi.Client.Outbox;

/// <summary>
/// A durable queue in front of an <see cref="IXapiClient"/>: Statements are stored before
/// <see cref="EnqueueAsync"/> returns, and delivered later, in order and in batches, by <see cref="FlushAsync"/>
/// or by a background loop (<see cref="RunAsync"/>).
/// </summary>
/// <remarks>
/// <para>
/// Delivery is at least once. Every Statement gets its id before it is stored, so a Statement sent again after a
/// lost response is the same Statement to the LRS, which keeps the first copy: on a conforming LRS the result is
/// exactly once.
/// </para>
/// <para>
/// Failed sends are classified (docs/outbox.md has the full table). Network errors, timeouts, 408, 429 and 5xx
/// are retried with exponential backoff and jitter, honouring <c>Retry-After</c>. A 400 for a batch splits it in
/// halves until the invalid Statement is found; only that one becomes a dead letter. A 409 is resolved by fetching
/// the stored Statement and comparing it with <see cref="StatementEquivalence"/>. A 413 halves the batch size.
/// 401, 403 and responses that mean the endpoint is wrong pause the outbox; nothing is dead-lettered.
/// </para>
/// <para>
/// All members are thread-safe. One flush runs at a time; Statements can be enqueued while it runs. Events are
/// raised on the thread that runs the flush, which is not the Unity main thread; exceptions thrown by handlers
/// are caught and ignored, so a handler cannot stop delivery.
/// </para>
/// <para>
/// Attachments are not supported: the outbox does not store attachment data, so Statements with an Attachment
/// that has no <c>fileUrl</c> are rejected by <see cref="EnqueueAsync"/>. Send those with
/// <see cref="IXapiClient.SaveStatementAsync"/>.
/// </para>
/// </remarks>
public sealed partial class StatementOutbox : IAsyncDisposable, IDisposable
{
    private const int MaxResponseBodyLength = 16 * 1024;

    private readonly Func<IXapiClient> _clientFactory;
    private readonly IOutboxStore _store;
    private readonly TimeProvider _time;
    private readonly UuidV7Generator _ids;
    private readonly int? _maxQueueSize;
    private readonly TimeSpan _initialRetryDelay;
    private readonly TimeSpan _maxRetryDelay;
    private readonly int? _maxAttempts;
    private readonly SemaphoreSlim _enqueueGate = new(1, 1);
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly CancellationTokenSource _disposed = new();
#if NET9_0_OR_GREATER
    private readonly Lock _stateGate = new();
#else
    private readonly object _stateGate = new();
#endif

    // Guarded by _stateGate
    private bool _paused;
    private bool _sending;
    private string? _lastError;
    private int _consecutiveFailures;
    private DateTimeOffset? _retryAt;
    private Random _random = new();

    // Changed only by the flush that holds _flushGate
    private int _batchSize;

    /// <summary>Creates an outbox.</summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="store">Where to keep the Statements: a <see cref="FileOutboxStore"/>, usually.</param>
    /// <param name="options">The settings, or <see langword="null"/> for the defaults. They are copied.</param>
    /// <exception cref="ArgumentException">An option is out of range.</exception>
    public StatementOutbox(IXapiClient client, IOutboxStore store, OutboxOptions? options = null)
        : this(ConstantFactory(Guard.NotNull(client)), store, options)
    {
    }

    /// <summary>Creates an outbox that asks <paramref name="clientFactory"/> for a client at the start of every flush.</summary>
    internal StatementOutbox(Func<IXapiClient> clientFactory, IOutboxStore store, OutboxOptions? options)
    {
        _clientFactory = Guard.NotNull(clientFactory);
        _store = Guard.NotNull(store);
        options ??= new OutboxOptions();
        if (options.BatchSize < 1)
        {
            throw new ArgumentException($"OutboxOptions.BatchSize must be at least 1, not {options.BatchSize}.", nameof(options));
        }

        if (options.MaxQueueSize < 1)
        {
            throw new ArgumentException($"OutboxOptions.MaxQueueSize must be at least 1 or null, not {options.MaxQueueSize}.", nameof(options));
        }

        if (options.MaxAttempts < 1)
        {
            throw new ArgumentException($"OutboxOptions.MaxAttempts must be at least 1 or null, not {options.MaxAttempts}.", nameof(options));
        }

        if (options.InitialRetryDelay <= TimeSpan.Zero || options.MaxRetryDelay < options.InitialRetryDelay)
        {
            throw new ArgumentException("OutboxOptions.InitialRetryDelay must be positive and not greater than MaxRetryDelay.", nameof(options));
        }

        _time = options.TimeProvider ?? throw new ArgumentException("OutboxOptions.TimeProvider must not be null.", nameof(options));
        _batchSize = options.BatchSize;
        _maxQueueSize = options.MaxQueueSize;
        _initialRetryDelay = options.InitialRetryDelay;
        _maxRetryDelay = options.MaxRetryDelay;
        _maxAttempts = options.MaxAttempts;
        _ids = new UuidV7Generator(_time.GetUtcNow);
    }

    /// <summary>Raised after Statements were delivered, or found already stored and equivalent at the LRS.</summary>
    public event EventHandler<OutboxDeliveredEventArgs>? Delivered;

    /// <summary>Raised after a Statement was moved to the dead letters.</summary>
    public event EventHandler<OutboxDeadLetteredEventArgs>? DeadLettered;

    /// <summary>Raised when the LRS refused a request and the outbox paused (<see cref="OutboxState.Paused"/>).</summary>
    public event EventHandler<OutboxPausedEventArgs>? Paused;

    /// <summary>For tests: replaces the source of jitter.</summary>
    internal Random Random
    {
        set
        {
            lock (_stateGate)
            {
                _random = value;
            }
        }
    }

    /// <summary>
    /// Stores a Statement for delivery. When the returned task completes, the Statement is in the store; with a
    /// <see cref="FileOutboxStore"/>, on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Statement without an id gets a new UUID version 7, and one without a timestamp gets the current time
    /// (to the millisecond), so that a Statement delivered hours later still says when it happened. The Statement
    /// is then validated with the xAPI 2.0.0 rules, which accept everything 1.0.3 does; a Statement that uses
    /// 2.0-only properties and is later sent to a 1.0.3 LRS becomes a dead letter.
    /// </para>
    /// <para>
    /// Enqueueing a Statement whose id is already waiting does nothing. The background loop, if running, is woken.
    /// </para>
    /// </remarks>
    /// <param name="statement">The Statement.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The Statement id.</returns>
    /// <exception cref="XapiValidationException">
    /// The Statement is invalid, or has an Attachment without a <c>fileUrl</c> (attachment data is not supported).
    /// </exception>
    /// <exception cref="OutboxFullException"><see cref="OutboxOptions.MaxQueueSize"/> Statements are already waiting.</exception>
    public async ValueTask<Guid> EnqueueAsync(Statement statement, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(statement);
        ThrowIfDisposed();
        var now = _time.GetUtcNow();
        var prepared = Prepare(statement, now);

        await _enqueueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_maxQueueSize is { } max && await _store.GetPendingCountAsync(cancellationToken).ConfigureAwait(false) >= max)
            {
                throw new OutboxFullException($"The outbox already holds {max} Statements (OutboxOptions.MaxQueueSize). The Statement was not stored.");
            }

            await _store.AddAsync(new OutboxEntry(prepared, now), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _enqueueGate.Release();
        }

        Wake();
        return prepared.Id!.Value;
    }

    /// <summary>
    /// Sends pending Statements now, batch after batch, until none are left, a transient error occurs, or the LRS
    /// refuses the request. Waits for a flush already in progress first.
    /// </summary>
    /// <remarks>
    /// A manual flush does not wait for the retry delay of an earlier failure. While the outbox is
    /// <see cref="OutboxState.Paused"/> it sends nothing; call <see cref="Resume"/> first. Errors are not thrown;
    /// they are in <see cref="OutboxFlushResult.Status"/>. Exceptions from the store are thrown.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the flush. A request in flight is abandoned; its Statements stay queued.</param>
    /// <returns>What the flush did and the state it left the outbox in.</returns>
    public async Task<OutboxFlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        FlushCounts counts;
        try
        {
            counts = await FlushCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _flushGate.Release();
        }

        // Wake the background loop, if any, so it re-reads the state this flush changed
        Wake();
        return new OutboxFlushResult
        {
            Delivered = counts.Delivered,
            DeadLettered = counts.DeadLettered,
            Status = await GetStatusAsync(cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Ends a pause (<see cref="OutboxState.Paused"/>), for example after the credentials were fixed, and wakes the
    /// background loop. Does nothing when the outbox is not paused.
    /// </summary>
    public void Resume()
    {
        lock (_stateGate)
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
            _lastError = null;
        }

        Wake();
    }

    /// <summary>Returns what the outbox is doing, how much is waiting, and the last error.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    public async ValueTask<OutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _store.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);
        var deadLetters = await _store.GetDeadLetterCountAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateGate)
        {
            return new OutboxStatus
            {
                State = _paused ? OutboxState.Paused
                    : _sending ? OutboxState.Sending
                    : _retryAt is not null ? OutboxState.WaitingToRetry
                    : OutboxState.Idle,
                PendingCount = pending,
                DeadLetterCount = deadLetters,
                LastError = _lastError,
                ConsecutiveFailures = _consecutiveFailures,
                RetryAt = _retryAt,
            };
        }
    }

    /// <summary>Returns the dead letters, oldest first.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The dead letters.</returns>
    public ValueTask<IReadOnlyList<OutboxDeadLetter>> GetDeadLettersAsync(CancellationToken cancellationToken = default) =>
        _store.GetDeadLettersAsync(cancellationToken);

    /// <summary>
    /// Moves a dead letter back to the end of the queue, with no failed attempts, and wakes the background loop.
    /// Use it after fixing what made the LRS reject the Statement.
    /// </summary>
    /// <param name="statementId">The Statement id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="false"/> when there is no dead letter with that id.</returns>
    public async ValueTask<bool> RequeueDeadLetterAsync(Guid statementId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var requeued = await _store.RequeueAsync(statementId, cancellationToken).ConfigureAwait(false);
        if (requeued)
        {
            Wake();
        }

        return requeued;
    }

    /// <summary>Moves every dead letter back to the end of the queue, oldest first, and wakes the background loop.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of dead letters requeued.</returns>
    public async ValueTask<int> RequeueAllDeadLettersAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var count = 0;
        foreach (var deadLetter in await _store.GetDeadLettersAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await _store.RequeueAsync(deadLetter.StatementId, cancellationToken).ConfigureAwait(false))
            {
                count++;
            }
        }

        if (count > 0)
        {
            Wake();
        }

        return count;
    }

    /// <summary>Deletes a dead letter for good.</summary>
    /// <param name="statementId">The Statement id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="false"/> when there is no dead letter with that id.</returns>
    public ValueTask<bool> RemoveDeadLetterAsync(Guid statementId, CancellationToken cancellationToken = default) =>
        _store.RemoveDeadLetterAsync(statementId, cancellationToken);

    /// <summary>
    /// Stops the background loop started with <see cref="RunAsync"/> and waits for it to end. The store and the
    /// client are not disposed. A flush started with <see cref="FlushAsync"/> is not waited for.
    /// </summary>
    /// <returns>A task that completes when the loop has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed.IsCancellationRequested)
        {
            return;
        }

        _disposed.Cancel();
        if (Volatile.Read(ref _loop) is { } loop)
        {
            await loop.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tells the background loop started with <see cref="RunAsync"/> to stop, without waiting for it. Prefer
    /// <see cref="DisposeAsync"/>, which waits. The store and the client are not disposed.
    /// </summary>
    public void Dispose() => _disposed.Cancel();

    private static Func<IXapiClient> ConstantFactory(IXapiClient client) => () => client;

    private Statement Prepare(Statement statement, DateTimeOffset now)
    {
        var prepared = statement;
        if (prepared.Id is null)
        {
            prepared = prepared with { Id = _ids.NewGuid() };
        }

        if (prepared.Timestamp is null)
        {
            prepared = prepared with { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(now.ToUnixTimeMilliseconds()) };
        }

        var errors = new List<XapiError>(StatementValidator.Validate(prepared, XapiVersion.V2_0_0));
        AddAttachmentErrors(prepared.Attachments, "$.attachments", errors);
        if (prepared.Object is SubStatement subStatement)
        {
            AddAttachmentErrors(subStatement.Attachments, "$.object.attachments", errors);
        }

        return errors.Count == 0 ? prepared : throw new XapiValidationException(errors);
    }

    private static void AddAttachmentErrors(XapiList<Attachment> attachments, string path, List<XapiError> errors)
    {
        for (var i = 0; i < attachments.Count; i++)
        {
            if (attachments[i].FileUrl is null)
            {
                errors.Add(new XapiError(
                    XapiErrorCodes.AttachmentPayloadMissing,
                    $"{path}[{i}]",
                    "The outbox does not store attachment data, so only Attachments with a fileUrl can be queued. Send this Statement with IXapiClient.SaveStatementAsync."));
            }
        }
    }

    private void Raise<T>(EventHandler<T>? handlers, T args)
        where T : EventArgs
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<T>)handler)(this, args);
            }
#pragma warning disable CA1031 // A failing handler must not interrupt delivery bookkeeping; documented on the class
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    private void ThrowIfDisposed()
    {
#if NET
        ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
#else
        if (_disposed.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(StatementOutbox));
        }
#endif
    }
}
