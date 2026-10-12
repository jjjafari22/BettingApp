using System;
using Microsoft.Extensions.Logging;

namespace BettingApp.Services
{
    /// <summary>
    /// In-memory notification service for real-time UI updates across Blazor Server components and background services.
    /// Replaces the fragile HTTP/WebSocket loopback SignalR client anti-pattern with direct in-process C# events.
    /// </summary>
    public class UiNotificationService
    {
        private readonly ILogger<UiNotificationService> _logger;

        public UiNotificationService(ILogger<UiNotificationService> logger)
        {
            _logger = logger;
        }

        public event Action<string?>? OnAdminNotification;
        public event Action<string, string?>? OnUserNotification;

        public void NotifyAdmin(string? message = null)
        {
            var handler = OnAdminNotification;
            if (handler == null) return;

            foreach (Action<string?> subscriber in handler.GetInvocationList())
            {
                try
                {
                    subscriber(message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error executing admin UI notification subscriber.");
                }
            }
        }

        public void NotifyUser(string userId, string? message = null)
        {
            if (string.IsNullOrEmpty(userId)) return;

            var handler = OnUserNotification;
            if (handler == null) return;

            foreach (Action<string, string?> subscriber in handler.GetInvocationList())
            {
                try
                {
                    subscriber(userId, message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error executing user UI notification subscriber for {UserId}.", userId);
                }
            }
        }
    }
}
