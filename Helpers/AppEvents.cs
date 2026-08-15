using System;

namespace FruitVegetableMarketPOS.Helpers
{
    /// <summary>
    /// Central pub/sub — any sale, payment, return, catalog, or customer change
    /// raises this so every screen can refresh instantly (even when not visible).
    /// </summary>
    public static class AppEvents
    {
        public static event Action? DataChanged;

        public static void NotifyChanged()
        {
            try { DataChanged?.Invoke(); }
            catch { /* ignore subscriber errors */ }
        }

        /// <summary>Marshal a refresh onto the WPF UI thread.</summary>
        public static void InvokeOnUi(Action action)
        {
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    action();
                    return;
                }

                if (dispatcher.CheckAccess())
                    action();
                else
                    dispatcher.Invoke(action);
            }
            catch { /* ignore */ }
        }
    }
}
