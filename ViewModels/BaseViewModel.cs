using System;
using System.Runtime.Versioning;
using System.ComponentModel;
using System.Media;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace GroceryPOS.ViewModels
{
    [SupportedOSPlatform("windows")]
    public class BaseViewModel : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // ── Popup Error ──
        private string _popupErrorMessage = "";
        public string PopupErrorMessage { get => _popupErrorMessage; set => SetProperty(ref _popupErrorMessage, value); }

        private bool _popupErrorVisible;
        public bool PopupErrorVisible { get => _popupErrorVisible; set => SetProperty(ref _popupErrorVisible, value); }

        protected async void ShowPopupError(string message)
        {
            PopupErrorMessage = message;
            PopupErrorVisible = true;
            SystemSounds.Hand.Play();
            await Task.Delay(2500);
            PopupErrorVisible = false;
        }

        // ── Popup Success ──
        private string _popupSuccessMessage = "";
        public string PopupSuccessMessage { get => _popupSuccessMessage; set => SetProperty(ref _popupSuccessMessage, value); }

        private bool _popupSuccessVisible;
        public bool PopupSuccessVisible { get => _popupSuccessVisible; set => SetProperty(ref _popupSuccessVisible, value); }

        protected async void ShowPopupSuccess(string message)
        {
            PopupSuccessMessage = message;
            PopupSuccessVisible = true;
            SystemSounds.Asterisk.Play();
            await Task.Delay(2500);
            PopupSuccessVisible = false;
        }

        /// <summary>
        /// Safely executes an action on the UI (Dispatcher) thread.
        /// </summary>
        protected void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.BeginInvoke(action);
            }
        }
        public virtual void Dispose()
        {
            // Optional: shared cleanup logic
        }
    }
}
