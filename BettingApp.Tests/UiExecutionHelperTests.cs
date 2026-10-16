using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BettingApp.Services;
using Xunit;

namespace BettingApp.Tests;

public class UiExecutionHelperTests
{
    [Fact]
    public async Task ExecuteWithMinDurationAsync_ExtendsShortOperationToMinDuration()
    {
        var sw = Stopwatch.StartNew();

        await UiExecutionHelper.ExecuteWithMinDurationAsync(async () =>
        {
            await Task.Delay(40);
        }, minDurationMs: 150);

        sw.Stop();

        // Should take at least ~140ms (accounting for timer granularity)
        Assert.True(sw.ElapsedMilliseconds >= 135, $"Elapsed was {sw.ElapsedMilliseconds}ms, expected >= 135ms");
    }

    [Fact]
    public async Task ExecuteWithMinDurationAsync_ReturnsResult_AndExtendsDuration()
    {
        var sw = Stopwatch.StartNew();

        var result = await UiExecutionHelper.ExecuteWithMinDurationAsync(async () =>
        {
            await Task.Delay(30);
            return 42;
        }, minDurationMs: 150);

        sw.Stop();

        Assert.Equal(42, result);
        Assert.True(sw.ElapsedMilliseconds >= 135, $"Elapsed was {sw.ElapsedMilliseconds}ms, expected >= 135ms");
    }

    [Fact]
    public async Task ExecuteWithMinDurationAsync_DoesNotExtendIfOperationExceedsMinDuration()
    {
        var sw = Stopwatch.StartNew();

        await UiExecutionHelper.ExecuteWithMinDurationAsync(async () =>
        {
            await Task.Delay(180);
        }, minDurationMs: 60);

        sw.Stop();

        // Should finish soon after operation finishes (~180-230ms, not 180 + 60)
        Assert.True(sw.ElapsedMilliseconds < 250, $"Elapsed was {sw.ElapsedMilliseconds}ms, expected < 250ms");
    }

    [Fact]
    public async Task ExecuteWithMinDurationAsync_ThrowsImmediatelyOnException()
    {
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await UiExecutionHelper.ExecuteWithMinDurationAsync(async () =>
            {
                await Task.Delay(20);
                throw new InvalidOperationException("Failed fast");
            }, minDurationMs: 500);
        });

        sw.Stop();

        // Must throw immediately, well under the 500ms minDuration
        Assert.True(sw.ElapsedMilliseconds < 250, $"Elapsed was {sw.ElapsedMilliseconds}ms, expected < 250ms");
    }

    [Fact]
    public async Task AwaitMinDurationAsync_WaitsWhenSuccessful()
    {
        var sw = Stopwatch.StartNew();
        var delayTask = UiExecutionHelper.StartDelay(150);

        // Simulate short DB work
        await Task.Delay(30);

        await UiExecutionHelper.AwaitMinDurationAsync(delayTask, isSuccess: true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 135, $"Elapsed was {sw.ElapsedMilliseconds}ms, expected >= 135ms");
    }

    [Fact]
    public async Task AwaitMinDurationAsync_SkipsWaitWhenNotSuccessful()
    {
        var sw = Stopwatch.StartNew();
        var delayTask = UiExecutionHelper.StartDelay(500);

        // Failed validation or error
        await UiExecutionHelper.AwaitMinDurationAsync(delayTask, isSuccess: false);
        sw.Stop();

        // Must exit immediately without waiting the 500ms
        Assert.True(sw.ElapsedMilliseconds < 100, $"Elapsed was {sw.ElapsedMilliseconds}ms, expected < 100ms");
    }

    [Fact]
    public async Task AwaitMinDurationAsync_HandlesNullGracefully()
    {
        await UiExecutionHelper.AwaitMinDurationAsync(null, isSuccess: true);
        await UiExecutionHelper.AwaitMinDurationAsync(null, isSuccess: false);
    }
}
