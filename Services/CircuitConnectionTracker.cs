using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace BettingApp.Services
{
    /// <summary>
    /// Tracks whether the current Blazor Server circuit has an active SignalR connection to the browser.
    /// Allows timers and background UI notifications to pause rendering and database refreshes while a
    /// mobile/desktop tab is disconnected (during the 30-minute DisconnectedCircuitRetentionPeriod) and
    /// cleanly catch up once the connection is restored.
    /// </summary>
    public sealed class CircuitConnectionTracker : CircuitHandler
    {
        private volatile bool _isConnected = true;
        private volatile bool _isClosed = false;

        public bool IsConnected => _isConnected && !_isClosed;
        public bool IsClosed => _isClosed;

        public event Action? OnConnectionRestored;

        public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            if (!_isClosed)
            {
                _isConnected = true;
            }
            return Task.CompletedTask;
        }

        public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            if (_isClosed) return Task.CompletedTask;

            bool wasDisconnected = !_isConnected;
            _isConnected = true;

            if (wasDisconnected)
            {
                try
                {
                    OnConnectionRestored?.Invoke();
                }
                catch
                {
                    // Individual subscribers handle their own component lifecycle exceptions
                }
            }

            return Task.CompletedTask;
        }

        public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _isConnected = false;
            return Task.CompletedTask;
        }

        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            _isConnected = false;
            _isClosed = true;
            return Task.CompletedTask;
        }
    }
}
