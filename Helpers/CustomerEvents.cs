using System;

namespace FruitVegetableMarketPOS.Helpers
{
    /// <summary>
    /// Pub/sub so Billing / Customers refresh pending-credit instantly after ledger payments.
    /// </summary>
    public static class CustomerEvents
    {
        public static event Action? CreditsChanged;

        public static void NotifyCreditsChanged()
        {
            try { CreditsChanged?.Invoke(); }
            catch { /* ignore subscriber errors */ }

            AppEvents.NotifyChanged();
        }
    }
}
