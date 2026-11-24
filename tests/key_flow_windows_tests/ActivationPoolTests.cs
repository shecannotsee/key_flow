using System.Collections.Concurrent;
using key_flow_app;
using key_flow_core;
using Xunit;

namespace key_flow_windows_tests;

public sealed class ActivationPoolTests
{
    [Fact]
    public async Task ActivationsRunInFifoOrderAndKeepProfileSnapshots()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new ConcurrentQueue<string>();
        var pool = new ActivationPool(async (profile, _) =>
        {
            started.Enqueue(profile.Name);
            if (profile.Name == "first") await gate.Task;
        });
        var firstProfile = new KeyProfile { Name = "first" };
        var first = pool.EnqueueAsync(firstProfile);
        firstProfile.Name = "edited after enqueue";
        var second = pool.EnqueueAsync(new KeyProfile { Name = "second" });

        await WaitUntilAsync(() => started.Count == 1);
        Assert.Equal("first", started.First());
        Assert.Equal(2, pool.ActiveCount);
        gate.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(["first", "second"], started);
        Assert.Equal(0, pool.ActiveCount);
        pool.Dispose();
    }

    [Fact]
    public async Task CancelAllCancelsCurrentAndQueuedActivations()
    {
        var started = new ConcurrentQueue<string>();
        var pool = new ActivationPool(async (profile, token) =>
        {
            started.Enqueue(profile.Name);
            await Task.Delay(Timeout.Infinite, token);
        });
        var first = pool.EnqueueAsync(new KeyProfile { Name = "first" });
        var second = pool.EnqueueAsync(new KeyProfile { Name = "second" });
        await WaitUntilAsync(() => started.Count == 1);

        pool.CancelAll();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(["first"], started);
        Assert.Equal(0, pool.ActiveCount);
        pool.Dispose();
    }

    [Fact]
    public async Task RepeatedActivationCoalescesAndRefreshesTimedWindow()
    {
        var started = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pool = new ActivationPool(async (_, token, _) =>
        {
            Interlocked.Increment(ref started);
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        var key = new object();
        var profile = new KeyProfile { Name = "timed", DurationSeconds = .35 };
        var first = pool.Submit(profile, key, RetriggerMode.ResetTimedRun);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(180);
        var repeated = pool.Submit(profile, key, RetriggerMode.ResetTimedRun);
        Assert.True(repeated.Coalesced);
        Assert.Same(first.Completion, repeated.Completion);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, started);
        Assert.Equal(0, pool.ActiveCount);
        pool.Dispose();
    }

    [Fact]
    public async Task IgnoreRetriggerCoalescesWithoutExtendingTheTimedWindow()
    {
        var started = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pool = new ActivationPool(async (_, token, _) =>
        {
            Interlocked.Increment(ref started);
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        var key = new object();
        var profile = new KeyProfile { Name = "timed", DurationSeconds = 1.2 };
        var first = pool.Submit(profile, key, RetriggerMode.IgnoreWhileActive);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(900);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var repeated = pool.Submit(profile, key, RetriggerMode.IgnoreWhileActive);
        Assert.True(repeated.Coalesced);
        Assert.Same(first.Completion, repeated.Completion);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.InRange(watch.ElapsedMilliseconds, 100, 800);
        Assert.Equal(1, started);
        pool.Dispose();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!predicate() && DateTime.UtcNow < timeout) await Task.Delay(10);
        Assert.True(predicate(), "Condition was not reached before timeout.");
    }
}
