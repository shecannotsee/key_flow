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

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!predicate() && DateTime.UtcNow < timeout) await Task.Delay(10);
        Assert.True(predicate(), "Condition was not reached before timeout.");
    }
}
