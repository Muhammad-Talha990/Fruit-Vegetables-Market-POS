using System;

namespace FruitVegetableMarketPOS.Helpers
{
    /// <summary>
    /// Centralised date-time helper for transactional consistency.
    ///
    /// USAGE PATTERN:
    ///   // At the TOP of any business operation / transaction:
    ///   DateTime txnTime = DateTimeHelper.CaptureTransactionTime();
    ///
    ///   // Then use txnTime for ALL inserts, updates, and ledger entries
    ///   // inside that same transaction — never call DateTime.Now again.
    ///
    /// This eliminates timestamp divergence when the CPU scheduler interrupts
    /// between multiple DateTime.Now calls within a single logical transaction.
    /// </summary>
    public static class DateTimeHelper
    {
        /// <summary>
        /// Captures the current local date-time ONCE.
        /// Store the returned value in a local variable and reuse it
        /// throughout the entire transaction / business operation.
        /// </summary>
        public static DateTime CaptureTransactionTime() => DateTime.Now;

        /// <summary>
        /// Returns the business date as yyyy-MM-dd (local time).
        /// </summary>
        public static string GetBusinessDate(DateTime? dt = null) =>
            (dt ?? DateTime.Today).ToString("yyyy-MM-dd");

        /// <summary>
        /// Returns the business date as a DateTime at midnight (local time).
        /// </summary>
        public static DateTime GetBusinessDateValue(DateTime? dt = null) =>
            (dt ?? DateTime.Today).Date;

        /// <summary>
        /// Formats a DateTime as an SQLite-compatible string.
        /// </summary>
        public static string ToDbString(this DateTime dt) =>
            dt.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
