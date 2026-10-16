using System;
using System.Threading.Tasks;

namespace BettingApp.Services;

public static class UiExecutionHelper
{
    public const int DefaultMinOverlayDurationMs = 700;

    public static Task StartDelay(int minDurationMs = DefaultMinOverlayDurationMs)
    {
        return Task.Delay(minDurationMs);
    }

    public static async Task AwaitMinDurationAsync(Task? delayTask, bool isSuccess = true)
    {
        if (isSuccess && delayTask != null)
        {
            try
            {
                await delayTask;
            }
            catch (OperationCanceledException)
            {
                // Ignored on cancellation or page navigation
            }
        }
    }

    public static async Task ExecuteWithMinDurationAsync(Func<Task> operation, int minDurationMs = DefaultMinOverlayDurationMs)
    {
        var delayTask = Task.Delay(minDurationMs);
        await operation();
        await delayTask;
    }

    public static async Task<T> ExecuteWithMinDurationAsync<T>(Func<Task<T>> operation, int minDurationMs = DefaultMinOverlayDurationMs)
    {
        var delayTask = Task.Delay(minDurationMs);
        var result = await operation();
        await delayTask;
        return result;
    }
}
