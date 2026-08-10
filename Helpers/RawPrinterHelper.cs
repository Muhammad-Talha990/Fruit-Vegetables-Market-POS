using System;
using System.Runtime.InteropServices;

namespace FruitVegetableMarketPOS.Helpers
{
    /// <summary>
    /// Sends raw bytes to a Windows printer (ESC/POS thermal).
    /// Avoids blank pages that many thermal drivers produce with GDI PrintDocument.
    /// </summary>
    public static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string? pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string? pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        public static bool SendBytesToPrinter(string printerName, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(printerName) || bytes == null || bytes.Length == 0)
                return false;

            if (!OpenPrinter(printerName.Trim(), out IntPtr hPrinter, IntPtr.Zero))
                return false;

            try
            {
                var di = new DOCINFOA
                {
                    pDocName = "FruitVegetableMarketPOS Receipt",
                    pDataType = "RAW"
                };

                if (!StartDocPrinter(hPrinter, 1, di))
                    return false;

                try
                {
                    if (!StartPagePrinter(hPrinter))
                        return false;

                    try
                    {
                        IntPtr pUnmanaged = Marshal.AllocCoTaskMem(bytes.Length);
                        try
                        {
                            Marshal.Copy(bytes, 0, pUnmanaged, bytes.Length);
                            if (!WritePrinter(hPrinter, pUnmanaged, bytes.Length, out int written))
                                return false;
                            return written == bytes.Length;
                        }
                        finally
                        {
                            Marshal.FreeCoTaskMem(pUnmanaged);
                        }
                    }
                    finally
                    {
                        EndPagePrinter(hPrinter);
                    }
                }
                finally
                {
                    EndDocPrinter(hPrinter);
                }
            }
            finally
            {
                ClosePrinter(hPrinter);
            }
        }
    }
}
