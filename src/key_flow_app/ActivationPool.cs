using key_flow_core;
using key_flow_windows;

namespace key_flow_app;

public sealed class ActivationPool : IDisposable
{
    private sealed record Request(KeyProfile Profile, CancellationToken Cancellation, TaskCompletionSource Completion);
    private const int Capacity = 64;
    private readonly object gate = new();
    private readonly Queue<Request> requests = new();
    private readonly Func<KeyProfile, CancellationToken, Task> execute;
    private CancellationTokenSource? current;
    private bool processing, disposed;

    public event Action? Changed;
    public event Action<Exception>? Failed;
    public int PendingCount { get { lock (gate) return requests.Count; } }
    public bool IsBusy { get { lock (gate) return processing; } }
    public int ActiveCount { get { lock (gate) return requests.Count + (current is null ? 0 : 1); } }

    public ActivationPool(Func<KeyProfile, CancellationToken, Task> execute) => this.execute = execute;

    public Task EnqueueAsync(KeyProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = ProfileOperations.Copy(profile, false);
        snapshot.Enabled = true;
        var request = new Request(snapshot, cancellationToken, new(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (requests.Count + (current is null ? 0 : 1) >= Capacity)
                throw new InvalidOperationException($"激活队列已满（最多 {Capacity} 个待执行方案）");
            requests.Enqueue(request);
            if (!processing)
            {
                processing = true;
                _ = Task.Run(ProcessAsync);
            }
        }
        Changed?.Invoke();
        return request.Completion.Task;
    }

    public void CancelAll()
    {
        lock (gate)
        {
            while (requests.TryDequeue(out var request)) request.Completion.TrySetCanceled();
            current?.Cancel();
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
                    processing = false;
                }
                else
                {
                    linked = CancellationTokenSource.CreateLinkedTokenSource(request.Cancellation);
                    current = linked;
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
                    await execute(request.Profile, linked.Token).ConfigureAwait(false);
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
                    lock (gate) if (ReferenceEquals(current, linked)) current = null;
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
