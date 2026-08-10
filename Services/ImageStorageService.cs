using System;
using System.IO;
using System.Threading.Tasks;
using FruitVegetableMarketPOS.Helpers;

namespace FruitVegetableMarketPOS.Services
{
    public class ImageStorageService : IImageStorageService
    {
        private static readonly string BillRootFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FruitVegetableMarketPOS", "Images", "Bills");

        private static readonly string ProductRootFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FruitVegetableMarketPOS", "Images", "Products");

        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB
        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png" };

        public ImageStorageService()
        {
            EnsureDirectoryExists(BillRootFolder);
            EnsureDirectoryExists(ProductRootFolder);
        }

        public async Task<string> SaveBillImageAsync(string sourceFilePath, string billId)
        {
            string destFileName = $"{billId}{ValidateAndGetExtension(sourceFilePath)}";
            string destPath = Path.Combine(BillRootFolder, destFileName);
            await CopyImageAsync(sourceFilePath, destPath);
            AppLogger.Info($"Bill image saved: {destPath}");
            return destPath;
        }

        public async Task<string> SaveProductImageAsync(string sourceFilePath, int itemId)
        {
            string destFileName = $"item-{itemId}{ValidateAndGetExtension(sourceFilePath)}";
            string destPath = Path.Combine(ProductRootFolder, destFileName);
            await CopyImageAsync(sourceFilePath, destPath);
            AppLogger.Info($"Product image saved: {destPath}");
            return destPath;
        }

        public void DeleteBillImage(string billId)
        {
            DeleteImageByPrefix(BillRootFolder, billId);
        }

        public string GetBillImagePath(string billId)
            => FindImagePath(BillRootFolder, billId);

        public string GetProductImagePath(int itemId)
            => FindImagePath(ProductRootFolder, $"item-{itemId}");

        private static string ValidateAndGetExtension(string sourceFilePath)
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("Source image file not found.", sourceFilePath);

            var fileInfo = new FileInfo(sourceFilePath);
            if (fileInfo.Length > MaxFileSizeBytes)
                throw new InvalidOperationException($"File size exceeds the 5MB limit ({fileInfo.Length / (1024 * 1024):N2} MB).");

            string extension = Path.GetExtension(sourceFilePath).ToLower();
            if (Array.IndexOf(AllowedExtensions, extension) < 0)
                throw new InvalidOperationException($"Invalid file type. Only {string.Join(", ", AllowedExtensions)} are allowed.");

            return extension;
        }

        private static async Task CopyImageAsync(string sourceFilePath, string destPath)
        {
            try
            {
                using var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
                await sourceStream.CopyToAsync(destStream);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to save image to {destPath}", ex);
                throw new InvalidOperationException("Could not save the image. Please check permissions.", ex);
            }
        }

        private static void DeleteImageByPrefix(string folder, string prefix)
        {
            try
            {
                foreach (var ext in AllowedExtensions)
                {
                    string path = Path.Combine(folder, $"{prefix}{ext}");
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        AppLogger.Info($"Deleted image: {path}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Error deleting image for prefix {prefix}", ex);
            }
        }

        private static string FindImagePath(string folder, string prefix)
        {
            foreach (var ext in AllowedExtensions)
            {
                string path = Path.Combine(folder, $"{prefix}{ext}");
                if (File.Exists(path)) return path;
            }
            return string.Empty;
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AppLogger.Info($"Created image storage directory: {path}");
            }
        }
    }
}
