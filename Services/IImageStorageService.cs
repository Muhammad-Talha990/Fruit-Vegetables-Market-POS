using System.Threading.Tasks;

namespace FruitVegetableMarketPOS.Services
{
    public interface IImageStorageService
    {
        /// <summary>
        /// Validates, renames, and saves an image to local storage.
        /// </summary>
        Task<string> SaveBillImageAsync(string sourceFilePath, string billId);

        /// <summary>
        /// Validates and saves a product image to local storage.
        /// </summary>
        Task<string> SaveProductImageAsync(string sourceFilePath, int itemId);

        /// <summary>
        /// Deletes the image associated with a Bill ID.
        /// </summary>
        void DeleteBillImage(string billId);

        /// <summary>
        /// Returns the full local path for a stored bill image.
        /// </summary>
        string GetBillImagePath(string billId);

        /// <summary>
        /// Returns the full local path for a stored product image.
        /// </summary>
        string GetProductImagePath(int itemId);
    }
}
