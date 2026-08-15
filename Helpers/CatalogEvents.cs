using System;

namespace FruitVegetableMarketPOS.Helpers
{
    /// <summary>
    /// Lightweight pub/sub so Items, types, prices, and photos refresh on Billing in realtime.
    /// </summary>
    public static class CatalogEvents
    {
        public static event Action? CatalogChanged;

        public static void NotifyChanged()
        {
            try { CatalogChanged?.Invoke(); }
            catch { /* ignore subscriber errors */ }

            AppEvents.NotifyChanged();
        }
    }
}
