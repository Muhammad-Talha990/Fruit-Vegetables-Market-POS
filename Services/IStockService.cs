using System.Collections.Generic;
using System.Threading.Tasks;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    public interface IStockService
    {
        event System.Action? StockChanged;
        void NotifyChanged();
        void DeductStock(string barcode, double quantity);
        void AddStock(string barcode, double quantity);

        // --- Cart-based Stock Purchase (new) ---
        /// <summary>
        /// Atomically saves a multi-item stock purchase cart.
        /// Inserts StockPurchases + StockPurchaseItems + InventoryLogs in one transaction.
        /// All records share the same timestamp (single DateTime.Now capture).
        /// The TotalAmount is automatically deducted from Cash in Drawer.
        /// </summary>
        Task<StockPurchase> RegisterPurchaseAsync(StockPurchase purchase, string? tempImagePath = null);

        // --- Legacy single-item supply management ---
        Task<bool> RegisterSupplyAsync(Stock entry, string? tempImagePath);
        Task<List<Stock>> GetSupplyHistoryAsync(string productId);
        Task<List<Stock>> GetAllRecentSuppliesAsync(int limit = 50);
        Task<bool> UpdateSupplyAsync(Stock entry, string? tempImagePath);
        Task<bool> DeleteSupplyAsync(int stockId);

        bool IsStockAvailable(string barcode, double requiredQuantity, out double availableQuantity);
        bool IsStockAvailable(int itemId, double requiredQuantity, out double availableQuantity);
        List<Item> GetLowStockItems();
        int GetLowStockCount();
    }
}
