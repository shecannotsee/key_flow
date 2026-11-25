using key_flow_core;
using key_flow_windows;

namespace key_flow_app;

public sealed class ActivationPool : IDisposable
{
    private sealed class Request(KeyProfile profile, CancellationToken cancellation, TaskCompletionSource completion,
        object? coalesceKey, ActivationWindow window)
    {
        public KeyProfile Profile { get; } = profile;
        public CancellationToken Cancellation { get; } = cancellation;
        public TaskCompletionSource Completion { get; } = completion;
        public object? CoalesceKey { get; } = coalesceKey;
        public ActivationWindow Window { get; } = window;
    }
    private const int Capacity = 64;
    private readonly object gate = new();
    private readonly Queue<Request> requests = new();
    private readonly Func<KeyProfile, CancellationToken, ActivationWindow, Task> execute;
    private CancellationTokenSource? current;
    private Request? currentRequest;
    private bool processing, disposed;

    public event Action? Changed;
    public event Action<Exception>? Failed;
    public int PendingCount { get { lock (gate) return requests.Count; } }
    public bool IsBusy { get { lock (gate) return processing; } }
    public int ActiveCount { get { lock (gate) return requests.Count + (current is null ? 0 : 1); } }

    public ActivationPool(Func<KeyProfile, CancellationToken, Task> execute)
        : this((profile, token, _) => execute(profile, token)) { }

    public ActivationPool(Func<KeyProfile, CancellationToken, ActivationWindow, Task> execute) => this.execute = execute;

    public Task EnqueueAsync(KeyProfile profile, CancellationToken cancellationToken = default)
        => Submit(profile, null, RetriggerMode.IgnoreWhileActive, cancellationToken).Completion;

    public ActivationSubmission Submit(KeyProfile profile, object? coalesceKey, RetriggerMode retriggerMode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = ProfileOperations.Copy(profile, false);
        snapshot.Enabled = true;
        var window = new ActivationWindow(snapshot.TotalDurationSeconds);
        var request = new Request(snapshot, cancellationToken, new(TaskCreationOptions.RunContinuationsAsynchronously), coalesceKey, window);
        lock (gate)
        {
            if (disposed) { window.Dispose(); ObjectDisposedException.ThrowIf(disposed, this); }
            if (coalesceKey is not null)
            {
                var existing = currentRequest is not null && ReferenceEquals(currentRequest.CoalesceKey, coalesceKey) &&
                    current?.IsCancellationRequested != true && !currentRequest.Window.IsExpired
                    ? currentRequest
                    : requests.FirstOrDefault(item => ReferenceEquals(item.CoalesceKey, coalesceKey) &&
                        !item.Cancellation.IsCancellationRequested && !item.Window.IsExpired);
                if (existing is not null)
                {
                    var reusable = !existing.Window.IsExpired && !existing.Cancellation.IsCancellationRequested &&
                        (retriggerMode != RetriggerMode.ResetTimedRun || existing.Window.Refresh());
                    if (reusable)
                    {
                        window.Dispose();
                        return new(existing.Completion.Task, true);
                    }
                }
            }
            if (requests.Count + (current is null ? 0 : 1) >= Capacity)
            {
                window.Dispose();
                throw new InvalidOperationException($"激活队列已满（最多 {Capacity} 个待执行方案）");
            }
            requests.Enqueue(request);
            if (!processing)
            {
                processing = true;
                _ = Task.Run(ProcessAsync);
            }
        }
        Changed?.Invoke();
        return new(request.Completion.Task, false);
    }

    public void CancelAll()
    {
        lock (gate)
        {
            while (requests.TryDequeue(out var request))
            {
                request.Completion.TrySetCanceled();
                request.Window.Dispose();
            }
            current?.Cancel();
        }
        Changed?.Invoke();
    }

    public void Cancel(object coalesceKey)
    {
        lock (gate)
        {
            var retained = new Queue<Request>();
            while (requests.TryDequeue(out var request))
            {
                if (ReferenceEquals(request.CoalesceKey, coalesceKey))
                {
                    request.Completion.TrySetCanceled();
                    request.Window.Dispose();
                }
                else retained.Enqueue(request);
            }
            while (retained.TryDequeue(out var request)) requests.Enqueue(request);
            if (ReferenceEquals(currentRequest?.CoalesceKey, coalesceKey)) current?.Cancel();
        }
        Changed?.Invoke();
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            Request? request;
            CancellationTokenSource? linked = null;
            lock (gate)
            {
                if (!requests.TryDequeue(out request))
                {
                    current = null;
                    currentRequest = null;
                    processing = false;
                }
                else
                {
                    linked = CancellationTokenSource.CreateLinkedTokenSource(request.Cancellation, request.Window.Token);
                    current = linked;
                    currentRequest = request;
                }
            }
            if (request is null)
            {
                Changed?.Invoke();
                return;
            }
            Changed?.Invoke();
            using (linked)
            {
                try
                {
                    linked!.Token.ThrowIfCancellationRequested();
                    request.Window.Start();
                    await execute(request.Profile, linked.Token, request.Window).ConfigureAwait(false);
                    request.Completion.TrySetResult();
                }
                catch (OperationCanceledException) when (linked!.IsCancellationRequested)
                {
                    request.Completion.TrySetCanceled(linked.Token);
                }
                catch (Exception exception)
                {
                    request.Completion.TrySetException(exception);
                    Failed?.Invoke(exception);
                }
                finally
                {
                    lock (gate)
                    {
                        if (ReferenceEquals(current, linked)) current = null;
                        if (ReferenceEquals(currentRequest, request)) currentRequest = null;
                    }
                    request.Window.Dispose();
                    Changed?.Invoke();
                }
            }
        }
    }

    public void Dispose()
    {
        lock (gate) disposed = true;
        CancelAll();
    }
}
