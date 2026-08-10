using System;

namespace FruitVegetableMarketPOS.Helpers
{
    /// <summary>
    /// Pub/sub so billing top-bar stats and Dashboard refresh instantly after sales/payments/returns.
    /// </summary>
    public static class SalesEvents
    {
        public static event Action? SalesChanged;

        public static void NotifyChanged()
        {
            try { SalesChanged?.Invoke(); }
            catch { /* ignore subscriber errors */ }
        }
    }
}
