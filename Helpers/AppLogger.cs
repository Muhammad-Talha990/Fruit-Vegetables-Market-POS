using System;
using System.IO;

namespace GroceryPOS.Helpers
{
    public static class AppLogger
    {
        private static readonly string LogDirectory;
        private static readonly object LockObj = new();

        static AppLogger()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            LogDirectory = Path.Combine(appData, "GroceryPOS", "logs");
            
            if (!Directory.Exists(LogDirectory))
                Directory.CreateDirectory(LogDirectory);
        }

        public static void Info(string message) => Log("INFO", message);
        public static void Warning(string message, Exception? ex = null)
        {
            var msg = message;
            if (ex != null)
            {
                msg += $" | Exception: {ex.Message}";
                if (ex.InnerException != null)
                    msg += $" | Inner Exception: {ex.InnerException.Message}";
            }
            Log("WARN", msg);
        }
        public static void Error(string message, Exception? ex = null)
        {
            var msg = message;
            if (ex != null)
            {
                msg += $" | Exception: {ex.Message}";
                if (ex.InnerException != null)
                    msg += $" | Inner Exception: {ex.InnerException.Message}";
                msg += $"\n{ex.StackTrace}";
            }
            Log("ERROR", msg);
        }

        private static void Log(string level, string message)
        {
            try
            {
                lock (LockObj)
                {
                    var logFile = Path.Combine(LogDirectory, $"log_{DateTime.Now:yyyy-MM-dd}.txt");
                    var entry = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(logFile, entry);
                }
            }
            catch
            {
                // Silently fail logging to prevent app crash
            }
        }
    }
}
