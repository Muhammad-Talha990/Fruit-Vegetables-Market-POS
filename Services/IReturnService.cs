using System.Collections.Generic;
using System.Threading.Tasks;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Services
{
    public interface IReturnService
    {
        Task<Bill?> GetBillForReturn(int billId);

        /// <summary>Processes a professional return transaction (Parent-Child).</summary>
        Task<Bill> CreateReturn(int originalBillId, int? userId, List<BillDescription> items);

        /// <summary>Gets a bill along with all its sequential return history.</summary>
        Task<(Bill Original, List<Bill> Returns)> GetBillWithReturnHistory(int billId);

        /// <summary>Validates if the requested return quantity is within original sale limits.</summary>
        Task<bool> ValidateReturnQuantity(int originalBillId, string productId, int requestedQuantity);

        /// <summary>Gets the total quantity already returned for a specific product.</summary>
        int GetTotalReturnedQuantity(int originalBillId, string productId);

        Task<Bill> ProcessReturn(int originalBillId, int? userId, List<BillDescription> itemsToReturn);
        int GetRemainingReturnableQuantity(int billId, string productId, int originalQuantity);
        Task<List<BillReturn>> GetReturnHistory(int billId);
    }
}
