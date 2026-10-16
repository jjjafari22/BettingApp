using System;
using Microsoft.JSInterop;
using System.Threading.Tasks;

namespace BettingApp.Services
{
    public class DialogService
    {
        private readonly IJSRuntime _js;
        private bool _isOpen;

        public DialogService(IJSRuntime js)
        {
            _js = js;
        }

        public async Task<bool> ConfirmAsync(string message)
        {
            if (_isOpen) return false;
            
            _isOpen = true;
            try
            {
                return await _js.InvokeAsync<bool>("confirm", message);
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException || ex is JSException || ex is JSDisconnectedException)
            {
                // If user cancelled, dialog timed out, or circuit disconnected, treat as not confirmed
                return false;
            }
            finally
            {
                _isOpen = false;
            }
        }
    }
}
