using o_down.Infrastructure;
using Xunit;

namespace o_down.Infrastructure.Tests;

public class SingleInstanceGuardTests
{
    [Fact]
    public void TryAcquire_FirstInstance_Succeeds()
    {
        var mutexName = "odown-singleton-test-" + Guid.NewGuid().ToString("N");
        using var guard = new SingleInstanceGuard(mutexName);
        Assert.True(guard.TryAcquire());
        Assert.True(guard.IsFirstInstance);
    }

    [Fact]
    public void TryAcquire_SecondInstance_Fails()
    {
        var mutexName = "odown-singleton-test-" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGuard(mutexName);
        Assert.True(first.TryAcquire());

        using var second = new SingleInstanceGuard(mutexName);
        Assert.False(second.TryAcquire());
        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public void TryAcquire_AfterFirstReleases_AllowsNewAcquisition()
    {
        var mutexName = "odown-singleton-test-" + Guid.NewGuid().ToString("N");
        var first = new SingleInstanceGuard(mutexName);
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var second = new SingleInstanceGuard(mutexName);
        Assert.True(second.TryAcquire());
    }

    [Fact]
    public async Task SendFocusMessage_IsReceived_ByFirstInstanceServer()
    {
        var mutexName = "odown-singleton-test-" + Guid.NewGuid().ToString("N");
        var pipeName = "odown-focus-test-" + Guid.NewGuid().ToString("N");
        using var guard = new SingleInstanceGuard(mutexName);
        Assert.True(guard.TryAcquire());

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        guard.StartFocusServer(pipeName, payload => received.TrySetResult(payload));
        await Task.Delay(150);

        var sent = await SingleInstanceGuard.SendFocusMessageAsync(pipeName, "show me the window", TimeSpan.FromSeconds(3));
        Assert.True(sent);
        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("show me the window", payload);
    }

    [Fact]
    public async Task SendFocusMessage_Timeout_WhenNoServer()
    {
        var pipeName = "odown-no-such-pipe-" + Guid.NewGuid().ToString("N");
        var sent = await SingleInstanceGuard.SendFocusMessageAsync(pipeName, "payload", TimeSpan.FromMilliseconds(250));
        Assert.False(sent);
    }

    [Fact]
    public void StartFocusServer_Throws_WhenNotFirstInstance()
    {
        var mutexName = "odown-singleton-test-" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGuard(mutexName);
        first.TryAcquire();

        using var second = new SingleInstanceGuard(mutexName);
        Assert.False(second.TryAcquire());
        Assert.Throws<InvalidOperationException>(() => second.StartFocusServer("any-pipe", _ => { }));
    }

    [Fact]
    public void MutexName_IsExposed()
    {
        var name = "odown-explicit-name";
        using var guard = new SingleInstanceGuard(name);
        Assert.Equal(name, guard.MutexName);
    }
}
