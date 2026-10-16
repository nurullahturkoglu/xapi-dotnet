namespace Xapi.Client.Internal;

/// <summary>
/// Holds the xAPI version of one LRS: fixed by configuration, or negotiated once and shared by every client that
/// uses this instance (the DI registration creates short-lived clients and shares one).
/// </summary>
internal sealed class VersionNegotiation
{
    private readonly object _lock = new();
    private Task<XapiVersion>? _task;

    public VersionNegotiation()
    {
    }

    public VersionNegotiation(XapiVersion version) => _task = Task.FromResult(version);

    /// <summary>The version, if it is fixed or a negotiation has finished.</summary>
    public XapiVersion? Known
    {
        get
        {
            var task = Volatile.Read(ref _task);
            return task is { Status: TaskStatus.RanToCompletion } ? task.Result : null;
        }
    }

    /// <summary>
    /// Returns the version, starting <paramref name="negotiate"/> if no negotiation has succeeded or is running.
    /// A failed negotiation is not cached, so the next call tries again.
    /// </summary>
    /// <remarks>
    /// <paramref name="negotiate"/> must not depend on the caller's cancellation token: other callers may be
    /// waiting for the same task. Each caller's token only cancels its own wait.
    /// </remarks>
    public Task<XapiVersion> GetAsync(Func<Task<XapiVersion>> negotiate, CancellationToken cancellationToken)
    {
        Task<XapiVersion> task;
        lock (_lock)
        {
            if (_task is null || _task.IsFaulted || _task.IsCanceled)
            {
                _task = negotiate();
            }

            task = _task;
        }

        return task.IsCompleted || !cancellationToken.CanBeCanceled ? task : WaitAsync(task, cancellationToken);
    }

    private static async Task<XapiVersion> WaitAsync(Task<XapiVersion> task, CancellationToken cancellationToken)
    {
#if NET
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
#else
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelled))
        {
            if (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        return await task.ConfigureAwait(false);
#endif
    }
}
