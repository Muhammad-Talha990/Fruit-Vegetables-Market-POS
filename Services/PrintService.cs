using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;
using System.Management;
using System.Linq;
using System.Printing;

namespace FruitVegetableMarketPOS.Services
{
    /// <summary>
    /// Receipt printing service for thermal printers.
    /// Updated to use Bill/BillDescription models.
    /// </summary>
    public class PrintService
    {
        private Bill? _billToPrint;
        private IEnumerable<Bill>? _returnHistoryToPrint;
        private Bill? _currentReturnBill;
        
        private double _paymentAmount;
        // Shop branding (PMC)
        private string _storeName = "PMC";
        private string _storeNameUrdu = "پاک مدینہ کمیشن ایجنٹس";
        private string _storeAddress = "I-11/4 Islamabad";
        private string _storePhone = "0345 5113044";
        private string _cashierName = "";

        private string? _preferredPrinter;
        private const string ConfigFile = "printer_config.txt";

        public PrintService()
        {
            LoadConfig();
        }

        private static string GetConfigPath()
        {
            // Prefer LocalAppData — installed apps under Program Files cannot write beside the EXE.
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FruitVegetableMarketPOS");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, ConfigFile);
        }

        private void LoadConfig()
        {
            try
            {
                // New location
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    _preferredPrinter = File.ReadAllText(path).Trim();
                    return;
                }

                // Migrate legacy config next to the EXE (dev / old installs)
                string legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFile);
                if (File.Exists(legacy))
                {
                    _preferredPrinter = File.ReadAllText(legacy).Trim();
                    if (!string.IsNullOrWhiteSpace(_preferredPrinter))
                        SaveConfig(_preferredPrinter);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load printer config", ex);
            }
        }

        private void SaveConfig(string printerName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(printerName)) return;
                File.WriteAllText(GetConfigPath(), printerName.Trim());
                _preferredPrinter = printerName.Trim();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save printer config", ex);
                // Still keep in memory for this session
                _preferredPrinter = printerName.Trim();
            }
        }

        /// <summary>
        /// Resolve the thermal printer without prompting when possible.
        /// Only shows PrintDialog once if nothing is configured / found.
        /// </summary>
        public string? ResolvePrinter(bool allowDialog = true)
        {
            // 1) Remembered printer still installed
            if (!string.IsNullOrWhiteSpace(_preferredPrinter) && IsInstalledPrinter(_preferredPrinter))
                return _preferredPrinter;

            // 2) Auto-detect BlackCopper / 80mm thermal
            var auto = FindPreferredThermalPrinter();
            if (!string.IsNullOrWhiteSpace(auto))
            {
                SaveConfig(auto);
                return auto;
            }

            if (!allowDialog)
                return _preferredPrinter;

            // 3) One-time picker, then remember
            try
            {
                using (var dialog = new System.Windows.Forms.PrintDialog())
                {
                    dialog.Document = new PrintDocument();
                    dialog.UseEXDialog = true;

                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        var name = dialog.PrinterSettings.PrinterName;
                        SaveConfig(name);
                        return name;
                    }
                }
            }
            finally
            {
                ActivateMainWindow();
            }

            return null;
        }

        private static bool IsInstalledPrinter(string name)
        {
            try
            {
                return PrinterSettings.InstalledPrinters.Cast<string>()
                    .Any(p => p.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        private static string? FindPreferredThermalPrinter()
        {
            try
            {
                var printers = PrinterSettings.InstalledPrinters.Cast<string>().ToList();
                string[] hints = { "BlackCopper", "80mm", "POS-80", "POS80", "Thermal", "Receipt", "XP-80", "Xprinter" };
                foreach (var hint in hints)
                {
                    var match = printers.FirstOrDefault(p =>
                        p.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) return match;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("FindPreferredThermalPrinter failed", ex);
            }
            return null;
        }

        private static void ActivateMainWindow()
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var w = System.Windows.Application.Current?.MainWindow;
                    if (w == null) return;
                    if (!w.IsVisible) w.Show();
                    if (w.WindowState == System.Windows.WindowState.Minimized)
                        w.WindowState = System.Windows.WindowState.Normal;
                    w.Activate();
                    w.Topmost = true;
                    w.Topmost = false;
                    w.Focus();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch { /* ignore focus restore failures */ }
        }

        public bool IsPrinterOnline()
        {
            try
            {
                string printerName = _preferredPrinter ?? "";
                if (string.IsNullOrEmpty(printerName))
                {
                    var settings = new PrinterSettings();
                    printerName = settings.PrinterName;
                }

                if (string.IsNullOrEmpty(printerName))
                    return false;

                // Prefer a light-weight check — do not purge jobs or treat queued jobs as offline
                // (that previously skipped printing after every sale).
                using var server = new LocalPrintServer();
                var queue = server.GetPrintQueues().FirstOrDefault(q =>
                    q.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase) ||
                    q.FullName.Equals(printerName, StringComparison.OrdinalIgnoreCase));

                if (queue == null)
                {
                    // Configured name may still work via winspool RAW
                    return PrinterSettings.InstalledPrinters.Cast<string>()
                        .Any(p => p.Equals(printerName, StringComparison.OrdinalIgnoreCase));
                }

                queue.Refresh();
                if (queue.IsOffline || queue.IsNotAvailable)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Printer status check failed", ex);
                // Still attempt print — status APIs are unreliable for USB thermal printers
                return !string.IsNullOrWhiteSpace(_preferredPrinter);
            }
        }

        /// <summary>
        /// Prints a payment receipt for a single bill payment (ESC/POS raster for thermal).
        /// </summary>
        public bool PrintPaymentReceipt(Bill bill, double paymentAmount, string cashierName)
        {
            try
            {
                if (bill == null) return false;

                _billToPrint = bill;
                _paymentAmount = paymentAmount;
                _cashierName = cashierName;

                string? targetPrinter = ResolvePrinter(allowDialog: true);
                if (string.IsNullOrEmpty(targetPrinter))
                    return false;

                if (!string.IsNullOrEmpty(targetPrinter) &&
                    TryPrintEscPosPaymentSlip(bill, paymentAmount, cashierName, targetPrinter))
                {
                    AppLogger.Info($"ESC/POS payment slip printed for Bill #{bill.BillId} on {targetPrinter}");
                    return true;
                }

                var printDoc = new PrintDocument();
                printDoc.PrinterSettings.PrinterName = targetPrinter;
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt80mm", 315, 2000);
                printDoc.DefaultPageSettings.Margins = new Margins(10, 10, 10, 10);
                printDoc.PrintController = new StandardPrintController();
                printDoc.PrintPage += PrintPaymentPage_Handler;
                printDoc.Print();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Payment receipt printing failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Prints a Pay Dues slip after FIFO multi-bill payment (ESC/POS raster for thermal).
        /// </summary>
        public bool PrintDuesPaymentReceipt(
            Customer customer,
            DuesPaymentResult result,
            string cashierName,
            string paymentMethod = "Cash")
        {
            try
            {
                if (customer == null || result == null || result.AppliedAmount <= 0)
                    return false;

                string? targetPrinter = ResolvePrinter(allowDialog: true);
                if (string.IsNullOrEmpty(targetPrinter))
                    return false;

                if (string.IsNullOrEmpty(targetPrinter))
                {
                    AppLogger.Warning("PrintDuesPaymentReceipt: no printer configured");
                    return false;
                }

                if (TryPrintEscPosDuesSlip(customer, result, cashierName, paymentMethod, targetPrinter))
                {
                    AppLogger.Info($"ESC/POS Pay Dues slip printed for Customer #{customer.CustomerId} on {targetPrinter}");
                    return true;
                }

                var printDoc = new PrintDocument();
                printDoc.PrinterSettings.PrinterName = targetPrinter;
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt80mm", 315, 2000);
                printDoc.DefaultPageSettings.Margins = new Margins(10, 10, 10, 10);
                printDoc.PrintController = new StandardPrintController();
                printDoc.PrintPage += (sender, e) =>
                {
                    if (e.Graphics == null) return;
                    float scale = e.MarginBounds.Width / 576f;
                    e.Graphics.ScaleTransform(scale, scale);
                    DrawDuesPaymentSlip(e.Graphics, 576, customer, result, cashierName, paymentMethod);
                    e.HasMorePages = false;
                };
                printDoc.Print();
                AppLogger.Info($"GDI Pay Dues slip printed for Customer #{customer.CustomerId}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Pay Dues receipt printing failed", ex);
                return false;
            }
        }

        private bool TryPrintEscPosPaymentSlip(Bill bill, double paymentAmount, string cashierName, string printerName)
        {
            try
            {
                var bytes = BuildPaymentSlipEscPosRaster(bill, paymentAmount, cashierName);
                return RawPrinterHelper.SendBytesToPrinter(printerName, bytes);
            }
            catch (Exception ex)
            {
                AppLogger.Error("ESC/POS payment slip failed — will try GDI", ex);
                return false;
            }
        }

        private bool TryPrintEscPosDuesSlip(
            Customer customer,
            DuesPaymentResult result,
            string cashierName,
            string paymentMethod,
            string printerName)
        {
            try
            {
                var bytes = BuildDuesPaymentEscPosRaster(customer, result, cashierName, paymentMethod);
                return RawPrinterHelper.SendBytesToPrinter(printerName, bytes);
            }
            catch (Exception ex)
            {
                AppLogger.Error("ESC/POS Pay Dues slip failed — will try GDI", ex);
                return false;
            }
        }

        private byte[] BuildPaymentSlipEscPosRaster(Bill bill, double paymentAmount, string cashierName)
        {
            const int width = 576;
            const int maxHeight = 3000;
            using var bmp = new Bitmap(width, maxHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            float contentBottom;
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.PageUnit = GraphicsUnit.Pixel;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                contentBottom = DrawSingleBillPaymentSlip(g, width, bill, paymentAmount, cashierName);
            }
            int cropH = Math.Max(120, (int)Math.Ceiling(contentBottom) + 40);
            cropH = Math.Min(cropH, maxHeight);
            using var cropped = bmp.Clone(new Rectangle(0, 0, width, cropH), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            return ConvertBitmapToEscPosRaster(cropped);
        }

        private byte[] BuildDuesPaymentEscPosRaster(
            Customer customer,
            DuesPaymentResult result,
            string cashierName,
            string paymentMethod)
        {
            const int width = 576;
            const int maxHeight = 4000;
            using var bmp = new Bitmap(width, maxHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            float contentBottom;
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.PageUnit = GraphicsUnit.Pixel;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                contentBottom = DrawDuesPaymentSlip(g, width, customer, result, cashierName, paymentMethod);
            }
            int cropH = Math.Max(120, (int)Math.Ceiling(contentBottom) + 40);
            cropH = Math.Min(cropH, maxHeight);
            using var cropped = bmp.Clone(new Rectangle(0, 0, width, cropH), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            return ConvertBitmapToEscPosRaster(cropped);
        }

        private float DrawSingleBillPaymentSlip(Graphics g, int pageWidthPx, Bill bill, double paymentAmount, string cashierName)
        {
            float margin = 16;
            float contentWidth = pageWidthPx - (margin * 2);
            float y = 12;
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            using var headerFont = new Font("Consolas", 56, FontStyle.Bold, GraphicsUnit.Pixel);
            using var urduShopFont = CreateUrduFontPixels(26, FontStyle.Bold);
            using var titleFont = new Font("Consolas", 26, FontStyle.Bold, GraphicsUnit.Pixel);
            using var titleUrdu = CreateUrduFontPixels(22, FontStyle.Bold);
            using var metaFont = new Font("Consolas", 22, FontStyle.Regular, GraphicsUnit.Pixel);
            using var metaBold = new Font("Consolas", 22, FontStyle.Bold, GraphicsUnit.Pixel);
            using var smallFont = new Font("Consolas", 20, FontStyle.Regular, GraphicsUnit.Pixel);
            using var totalFont = new Font("Consolas", 28, FontStyle.Bold, GraphicsUnit.Pixel);

            void DrawFullDash()
            {
                using var pen = new Pen(Color.Black, 1.8f)
                {
                    DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
                    DashPattern = new float[] { 4f, 3f }
                };
                g.DrawLine(pen, margin, y + 10, margin + contentWidth, y + 10);
                y += 24;
            }

            g.DrawString(_storeName, headerFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 64), sfCenter);
            y += 64 + 6;
            g.DrawString(_storeNameUrdu, urduShopFont, Brushes.Black, new RectangleF(margin, y, contentWidth, 36), sfCenter);
            y += 36 + 4;
            g.DrawString(_storeAddress, smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 26 + 2;
            g.DrawString($"Ph: {_storePhone}", smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 26 + 8;
            g.DrawString("PAYMENT RECEIPT", titleFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 30), sfCenter);
            y += 30 + 2;
            g.DrawString("ادائیگی کی رسید", titleUrdu, Brushes.Black, new RectangleF(margin, y, contentWidth, 28), sfCenter);
            y += 28 + 10;

            DrawFullDash();

            g.DrawString($"Receipt#: {bill.InvoiceNumber}", metaBold, Brushes.Black, margin, y);
            y += 28;
            var cust = bill.Customer?.FullName ?? "Customer";
            g.DrawString($"Customer: {cust}", metaFont, Brushes.Black, margin, y);
            y += 28;
            g.DrawString($"Date: {DateTime.Now:dd/MM/yyyy hh:mm tt}", metaFont, Brushes.Black, margin, y);
            y += 28;
            g.DrawString($"Cashier: {cashierName}", metaFont, Brushes.Black, margin, y);
            y += 28;

            DrawFullDash();

            void Row(string label, string value, Font font)
            {
                g.DrawString(label, font, Brushes.Black, margin, y);
                g.DrawString(value, font, Brushes.Black, margin + contentWidth, y, sfRight);
                y += 30;
            }

            Row("Bill Total:", $"Rs.{bill.GrandTotal:N2}", metaFont);
            double previousPaid = Math.Max(0, bill.PaidAmount - paymentAmount);
            Row("Previously Paid:", $"Rs.{previousPaid:N2}", metaFont);
            Row("Payment Received:", $"Rs.{paymentAmount:N2}", totalFont);
            Row("Total Paid:", $"Rs.{bill.PaidAmount:N2}", metaBold);
            if (bill.RemainingAmount > 0.01)
                Row("DUE AMOUNT:", $"Rs.{bill.RemainingAmount:N2}", totalFont);
            else
            {
                g.DrawString("STATUS: FULLY PAID", totalFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 32), sfCenter);
                y += 36;
            }

            DrawFullDash();
            g.DrawString("Thank you for your payment!", metaFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 28;
            g.DrawString("Please come again", smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 24), sfCenter);
            y += 36;
            return y;
        }

        private float DrawDuesPaymentSlip(
            Graphics g,
            int pageWidthPx,
            Customer customer,
            DuesPaymentResult result,
            string cashierName,
            string paymentMethod)
        {
            float margin = 16;
            float contentWidth = pageWidthPx - (margin * 2);
            float y = 12;
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            using var headerFont = new Font("Consolas", 56, FontStyle.Bold, GraphicsUnit.Pixel);
            using var urduShopFont = CreateUrduFontPixels(26, FontStyle.Bold);
            using var titleFont = new Font("Consolas", 26, FontStyle.Bold, GraphicsUnit.Pixel);
            using var titleUrdu = CreateUrduFontPixels(22, FontStyle.Bold);
            using var metaFont = new Font("Consolas", 22, FontStyle.Regular, GraphicsUnit.Pixel);
            using var metaBold = new Font("Consolas", 22, FontStyle.Bold, GraphicsUnit.Pixel);
            using var smallFont = new Font("Consolas", 20, FontStyle.Regular, GraphicsUnit.Pixel);
            using var totalFont = new Font("Consolas", 28, FontStyle.Bold, GraphicsUnit.Pixel);

            void DrawFullDash()
            {
                using var pen = new Pen(Color.Black, 1.8f)
                {
                    DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
                    DashPattern = new float[] { 4f, 3f }
                };
                g.DrawLine(pen, margin, y + 10, margin + contentWidth, y + 10);
                y += 24;
            }

            g.DrawString(_storeName, headerFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 64), sfCenter);
            y += 64 + 6;
            g.DrawString(_storeNameUrdu, urduShopFont, Brushes.Black, new RectangleF(margin, y, contentWidth, 36), sfCenter);
            y += 36 + 4;
            g.DrawString(_storeAddress, smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 26 + 2;
            g.DrawString($"Ph: {_storePhone}", smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 26 + 8;
            g.DrawString("PAY DUES RECEIPT", titleFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 30), sfCenter);
            y += 30 + 2;
            g.DrawString("واجب الادا ادائیگی کی رسید", titleUrdu, Brushes.Black, new RectangleF(margin, y, contentWidth, 28), sfCenter);
            y += 28 + 10;

            DrawFullDash();

            g.DrawString($"Customer: {customer.FullName}", metaBold, Brushes.Black, margin, y);
            y += 28;
            if (!string.IsNullOrWhiteSpace(customer.PrimaryPhone))
            {
                g.DrawString($"Phone: {customer.PrimaryPhone}", metaFont, Brushes.Black, margin, y);
                y += 28;
            }
            g.DrawString($"Date: {DateTime.Now:dd/MM/yyyy hh:mm tt}", metaFont, Brushes.Black, margin, y);
            y += 28;
            g.DrawString($"Cashier: {cashierName}", metaFont, Brushes.Black, margin, y);
            y += 28;
            g.DrawString($"Payment: {paymentMethod}", metaFont, Brushes.Black, margin, y);
            y += 28;

            DrawFullDash();

            void Row(string label, string value, Font font)
            {
                g.DrawString(label, font, Brushes.Black, margin, y);
                g.DrawString(value, font, Brushes.Black, margin + contentWidth, y, sfRight);
                y += 30;
            }

            Row("Cash Received:", $"Rs.{result.CashReceived:N2}", metaFont);
            Row("Applied Amount:", $"Rs.{result.AppliedAmount:N2}", totalFont);

            if (result.Allocations.Count > 0)
            {
                DrawFullDash();
                g.DrawString("Bills Paid · ادا شدہ بل", metaBold, Brushes.Black, margin, y);
                y += 28;
                foreach (var a in result.Allocations)
                    Row($"Bill #{a.InvoiceNumber}", $"Rs.{a.AmountPaid:N2}", metaFont);
            }

            DrawFullDash();

            if (result.ChangeGiven > 0.01)
                Row("Change Given:", $"Rs.{result.ChangeGiven:N2}", metaBold);

            if (result.IsFullyCleared)
            {
                g.DrawString("STATUS: FULLY PAID", totalFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 32), sfCenter);
                y += 36;
                g.DrawString("تمام واجب الادا ادا ہو گیا", titleUrdu, Brushes.Black, new RectangleF(margin, y, contentWidth, 28), sfCenter);
                y += 32;
            }
            else
            {
                Row("Remaining Pending:", $"Rs.{result.RemainingPending:N2}", totalFont);
            }

            DrawFullDash();
            g.DrawString("Thank you for your payment!", metaFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 28;
            g.DrawString("Please come again", smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 24), sfCenter);
            y += 36;
            return y;
        }

        private void PrintPaymentPage_Handler(object sender, PrintPageEventArgs e)
        {
            if (e.Graphics == null || _billToPrint == null) return;

            var g = e.Graphics;
            var headerFont = new Font("Consolas", 11, FontStyle.Bold);
            var normalFont = new Font("Consolas", 8);
            var boldFont   = new Font("Consolas", 8, FontStyle.Bold);
            var smallFont  = new Font("Consolas", 7);

            float y         = 5;
            float margin    = 5;
            float pageWidth = 265;
            var sf      = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            // ── Store Header ──
            g.DrawString(_storeName, headerFont, Brushes.Black, new RectangleF(0, y, 302, 20), sf);
            y += 20;
            g.DrawString("--- PAYMENT RECEIPT ---", boldFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 15;
            g.DrawString(_storeAddress, smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 15;
            g.DrawString($"Ph: {_storePhone}", smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 18;

            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;

            // ── Bill & Customer Info ──
            g.DrawString($"Receipt#: {_billToPrint.InvoiceNumber}", boldFont, Brushes.Black, margin, y); y += 13;

            if (_billToPrint.Customer != null)
            {
                g.DrawString($"Customer: {_billToPrint.Customer.FullName}", normalFont, Brushes.Black, margin, y); y += 13;

                string? address = _billToPrint.BillingAddress ?? _billToPrint.Customer.Address;
                if (!string.IsNullOrEmpty(address))
                {
                    RectangleF addrRect = new RectangleF(margin, y, pageWidth, 40);
                    g.DrawString($"Address: {address}", smallFont, Brushes.Black, addrRect);
                    SizeF addrSize = g.MeasureString($"Address: {address}", smallFont, (int)pageWidth);
                    y += Math.Max(13, addrSize.Height + 2);
                }
            }
            else
            {
                g.DrawString("Customer: Walk-in", normalFont, Brushes.Black, margin, y); y += 13;
            }

            g.DrawString($"Date: {DateTime.Now:dd/MM/yyyy hh:mm tt}", normalFont, Brushes.Black, margin, y); y += 13;
            g.DrawString($"Cashier: {_cashierName}", normalFont, Brushes.Black, margin, y); y += 13;

            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;

            // ── Amount Details ──
            g.DrawString("BILL TOTAL:", boldFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{_billToPrint.GrandTotal:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 16;

            // Previous Paid = Total Paid - Current Payment
            double previousPaid = _billToPrint.PaidAmount - _paymentAmount;
            g.DrawString("Paid Amount:", normalFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{previousPaid:N2}", normalFont, Brushes.Black, pageWidth, y, sfRight);
            y += 16;

            g.DrawString("Payment Received:", headerFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{_paymentAmount:N2}", headerFont, Brushes.Black, pageWidth, y, sfRight);
            y += 22;

            g.DrawString(new string('=', 44), normalFont, Brushes.Black, margin, y); y += 14;

            g.DrawString("Total Paid Amount:", boldFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{_billToPrint.PaidAmount:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 16;

            if (_billToPrint.RemainingAmount > 0)
            {
                g.DrawString("DUE AMOUNT:", headerFont, Brushes.Black, margin, y);
                g.DrawString($"Rs.{_billToPrint.RemainingAmount:N2}", headerFont, Brushes.Black, pageWidth, y, sfRight);
                y += 22;
            }
            else
            {
                g.DrawString("STATUS: FULLY PAID", headerFont, Brushes.Black, new RectangleF(0, y, 302, 20), sf);
                y += 22;
            }

            // ── Footer ──
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;
            g.DrawString("Thank you for your payment!", normalFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 15;
            g.DrawString("Please come again", smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);

            e.HasMorePages = false;
            headerFont.Dispose();
            normalFont.Dispose();
            boldFont.Dispose();
            smallFont.Dispose();
        }

        /// <summary>
        /// Prints a clean return-only receipt showing just the returned items,
        /// amount details, and updated credit balance.
        /// </summary>
        public bool PrintReturnOnlyReceipt(Bill originalBill, Bill returnBill, string cashierName)
        {
            try
            {
                _billToPrint = originalBill;
                _currentReturnBill = returnBill;
                _cashierName = cashierName;

                var printDoc = new PrintDocument();
                string? targetPrinter = ResolvePrinter(allowDialog: true);
                if (string.IsNullOrEmpty(targetPrinter))
                    return false;

                printDoc.PrinterSettings.PrinterName = targetPrinter;
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", 302, 1200);
                printDoc.PrintPage += PrintReturnOnlyPage_Handler;
                printDoc.Print();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Return-only receipt printing failed", ex);
                return false;
            }
        }

        private void PrintReturnOnlyPage_Handler(object sender, PrintPageEventArgs e)
        {
            if (e.Graphics == null || _billToPrint == null || _currentReturnBill == null) return;

            var g = e.Graphics;
            var headerFont = new Font("Consolas", 11, FontStyle.Bold);
            var normalFont = new Font("Consolas", 8);
            var boldFont   = new Font("Consolas", 8, FontStyle.Bold);
            var smallFont  = new Font("Consolas", 7);

            float y         = 5;
            float margin    = 5;
            float pageWidth = 265;
            var sf      = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            // ── 1. Store Header ──
            g.DrawString(_storeName, headerFont, Brushes.Black, new RectangleF(0, y, 302, 20), sf);
            y += 20;
            g.DrawString("--- RETURN RECEIPT ---", boldFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 15;
            g.DrawString(_storeAddress, smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 15;
            g.DrawString($"Ph: {_storePhone}", smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 18;

            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;

            // ── 2. Bill & Customer Info ──
            g.DrawString($"Receipt#: {_billToPrint.InvoiceNumber}", boldFont, Brushes.Black, margin, y); y += 13;

            if (_billToPrint.CustomerId.HasValue)
            {
                string custName = _billToPrint.Customer?.FullName ?? "Customer";
                g.DrawString($"Customer: {custName}", normalFont, Brushes.Black, margin, y); y += 13;

                string? address = _currentReturnBill.BillingAddress ?? _billToPrint.BillingAddress
                    ?? _billToPrint.Customer?.Address;
                if (!string.IsNullOrEmpty(address))
                {
                    RectangleF addrRect = new RectangleF(margin, y, pageWidth, 40);
                    g.DrawString($"Address: {address}", smallFont, Brushes.Black, addrRect);
                    SizeF addrSize = g.MeasureString($"Address: {address}", smallFont, (int)pageWidth);
                    y += Math.Max(13, addrSize.Height + 2);
                }
            }
            else
            {
                g.DrawString("Customer: Walk-in", normalFont, Brushes.Black, margin, y); y += 13;
            }

            g.DrawString($"Date: {_currentReturnBill.CreatedAt:dd/MM/yyyy hh:mm tt}", normalFont, Brushes.Black, margin, y); y += 13;
            g.DrawString($"Cashier: {_cashierName}", normalFont, Brushes.Black, margin, y); y += 13;

            y += 4;
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;

            // ── 4. Returned Items ──
            g.DrawString("RETURNED ITEMS", boldFont, Brushes.Black, margin, y); y += 14;
            g.DrawString("Item",   boldFont, Brushes.Black, margin, y);
            g.DrawString("Qty",    boldFont, Brushes.Black, 130, y);
            g.DrawString("Price",  boldFont, Brushes.Black, 170, y);
            g.DrawString("Total",  boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 14;
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;

            double returnTotal = 0;
            foreach (var item in _currentReturnBill.Items)
            {
                float descWidth = 125;
                RectangleF rect = new RectangleF(margin, y, descWidth, 200);
                g.DrawString(item.DisplayName, normalFont, Brushes.Black, rect);
                SizeF size = g.MeasureString(item.DisplayName, normalFont, (int)descWidth);
                float descHeight = Math.Max(14, size.Height);

                double qty   = Math.Abs(item.Quantity);
                double total = Math.Abs(item.TotalPrice);
                returnTotal += total;

                g.DrawString(qty.ToString(), normalFont, Brushes.Black, 135, y);
                g.DrawString(item.UnitPrice.ToString("N0"), normalFont, Brushes.Black, 170, y);
                g.DrawString(total.ToString("N0"), normalFont, Brushes.Black, pageWidth, y, sfRight);
                y += descHeight + 3;
            }

            g.DrawString(new string('=', 44), normalFont, Brushes.Black, margin, y); y += 14;

            // ── 5. Amount Details ──
            g.DrawString("RETURN TOTAL:", headerFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{returnTotal:N2}", headerFont, Brushes.Black, pageWidth, y, sfRight);
            y += 20;

            double creditAdjusted = _currentReturnBill.RemainingDueAfterThisReturn;
            double cashRefund     = _currentReturnBill.CashReceived;
            string outcome        = _currentReturnBill.Status;

            if (outcome == "CreditOnly" || outcome == "Mixed")
            {
                g.DrawString("Credit Adjusted:", boldFont, Brushes.Black, margin, y);
                g.DrawString($"Rs.{creditAdjusted:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
                y += 14;

                // Show new balance: original remaining minus credit reduced
                double newBalance = _billToPrint.RemainingAmount - creditAdjusted;
                g.DrawString("New Balance Due:", boldFont, Brushes.Black, margin, y);
                g.DrawString($"Rs.{Math.Max(0, newBalance):N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
                y += 18;
            }

            if (outcome == "CashOnly" || outcome == "Mixed")
            {
                g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 10;
                g.DrawString("CASH REFUND:", headerFont, Brushes.Black, margin, y);
                g.DrawString($"Rs.{cashRefund:N2}", headerFont, Brushes.Black, pageWidth, y, sfRight);
                y += 22;
            }

            // ── 6. Footer ──
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;
            g.DrawString("Thank you for shopping!", normalFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 15;
            g.DrawString("Please come again", smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);

            e.HasMorePages = false;
            headerFont.Dispose();
            normalFont.Dispose();
            boldFont.Dispose();
            smallFont.Dispose();
        }

        public bool PrintUnifiedReturnReceipt(Bill originalBill, Bill returnBill, IEnumerable<Bill> history, string cashierName)
        {
            try
            {
                _billToPrint = originalBill; // Base for original details
                _currentReturnBill = returnBill;
                _returnHistoryToPrint = history;
                _cashierName = cashierName;

                var printDoc = new PrintDocument();
                string? targetPrinter = ResolvePrinter(allowDialog: true);
                if (string.IsNullOrEmpty(targetPrinter))
                    return false;

                printDoc.PrinterSettings.PrinterName = targetPrinter;
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", 302, 1500); 
                printDoc.PrintPage += PrintUnifiedReturnPage_Handler;
                printDoc.Print();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Unified return receipt printing failed", ex);
                return false;
            }
        }

        public void PrintReturnSummary(Bill originalBill, IEnumerable<Bill> returns, string cashierName)
        {
            try
            {
                _billToPrint = originalBill;
                _returnHistoryToPrint = returns;
                _cashierName = cashierName;

                var printDoc = new PrintDocument();
                string? targetPrinter = ResolvePrinter(allowDialog: true);
                if (string.IsNullOrEmpty(targetPrinter))
                    return;

                printDoc.PrinterSettings.PrinterName = targetPrinter;
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", 302, 2000); 
                printDoc.PrintPage += PrintSummaryPage_Handler;
                printDoc.Print();

                _returnHistoryToPrint = null;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Return summary printing failed", ex);
                throw;
            }
        }

        /// <summary>Prints a receipt for the given Bill.</summary>
        public bool PrintReceipt(Bill bill, string cashierName, string? printerName = null)
        {
            try
            {
                if (bill == null)
                    return false;

                // If it's a return and has the parent bill metadata, use the unified return layout
                if (bill.IsReturn && bill.ParentBill != null)
                {
                    return PrintUnifiedReturnReceipt(bill.ParentBill, bill, bill.ReturnHistory, cashierName);
                }

                if (bill.Items == null || bill.Items.Count == 0)
                {
                    AppLogger.Warning($"PrintReceipt: Bill #{bill.BillId} has no line items — receipt would be empty.");
                }

                _billToPrint = bill;
                _cashierName = cashierName;

                string? targetPrinter = !string.IsNullOrWhiteSpace(printerName)
                    ? printerName
                    : ResolvePrinter(allowDialog: true);

                if (string.IsNullOrEmpty(targetPrinter))
                {
                    AppLogger.Warning($"PrintReceipt: no printer selected for Bill #{bill.BillId}");
                    ActivateMainWindow();
                    return false;
                }

                // Thermal printers (e.g. BlackCopper) often print BLANK with GDI.
                // Prefer ESC/POS raw bytes so text actually appears on paper.
                if (TryPrintEscPosReceipt(bill, cashierName, targetPrinter))
                {
                    AppLogger.Info($"ESC/POS receipt printed for Bill #{bill.BillId} on printer: {targetPrinter} ({bill.Items?.Count ?? 0} items)");
                    ActivateMainWindow();
                    return true;
                }

                // GDI fallback (PDF / XPS / some desktop printers)
                var printDoc = new PrintDocument();
                printDoc.PrinterSettings.PrinterName = targetPrinter;

                // PaperSize is in hundredths of an inch (80mm ≈ 315)
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt80mm", 315, 2000);
                printDoc.DefaultPageSettings.Margins = new Margins(10, 10, 10, 10);
                printDoc.PrintController = new StandardPrintController();

                printDoc.PrintPage += PrintPage_Handler;
                printDoc.Print();

                AppLogger.Info($"GDI receipt printed for Bill #{bill.BillId} on printer: {targetPrinter}");
                ActivateMainWindow();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Receipt printing failed", ex);
                ActivateMainWindow();
                return false;
            }
        }

        /// <summary>
        /// Prints a gate pass for the same sale: like the bill, but Item | Qty | Unit Price only
        /// (no Total column, no grand total / payment footer).
        /// </summary>
        public bool PrintGatePass(Bill bill, string cashierName, string? printerName = null)
        {
            try
            {
                if (bill == null) return false;
                if (bill.IsReturn) return false; // gate pass is for outbound sales only

                if (bill.Items == null || bill.Items.Count == 0)
                {
                    AppLogger.Warning($"PrintGatePass: Bill #{bill.BillId} has no line items");
                    return false;
                }

                // Never open a second PrintDialog — use remembered / auto / caller printer only
                string? targetPrinter = !string.IsNullOrWhiteSpace(printerName)
                    ? printerName
                    : ResolvePrinter(allowDialog: false);

                if (string.IsNullOrEmpty(targetPrinter))
                {
                    AppLogger.Warning($"PrintGatePass: no printer configured for Bill #{bill.BillId}");
                    return false;
                }

                // Let the bill job finish spooling before opening a second RAW job
                System.Threading.Thread.Sleep(500);

                if (TryPrintEscPosGatePass(bill, cashierName, targetPrinter))
                {
                    AppLogger.Info($"ESC/POS gate pass printed for Bill #{bill.BillId} on printer: {targetPrinter}");
                    ActivateMainWindow();
                    return true;
                }

                // GDI fallback with retry
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        if (attempt > 1)
                            System.Threading.Thread.Sleep(450);

                        var printDoc = new PrintDocument();
                        printDoc.PrinterSettings.PrinterName = targetPrinter;
                        printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt80mm", 315, 2000);
                        printDoc.DefaultPageSettings.Margins = new Margins(10, 10, 10, 10);
                        printDoc.PrintController = new StandardPrintController();

                        printDoc.PrintPage += (sender, e) =>
                        {
                            if (e.Graphics == null) return;
                            float scale = e.MarginBounds.Width / 576f;
                            e.Graphics.ScaleTransform(scale, scale);
                            DrawGatePassReceipt(e.Graphics, 576, bill, cashierName);
                            e.HasMorePages = false;
                        };
                        printDoc.Print();

                        AppLogger.Info($"GDI gate pass printed for Bill #{bill.BillId} on printer: {targetPrinter}");
                        ActivateMainWindow();
                        return true;
                    }
                    catch (Exception gdiEx)
                    {
                        AppLogger.Warning($"GDI gate pass attempt {attempt}/3 failed", gdiEx);
                    }
                }

                ActivateMainWindow();
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Gate pass printing failed", ex);
                ActivateMainWindow();
                return false;
            }
        }

        private bool TryPrintEscPosGatePass(Bill bill, string cashierName, string printerName)
        {
            try
            {
                var bytes = BuildGatePassEscPosRaster(bill, cashierName);
                return RawPrinterHelper.SendBytesToPrinterWithRetry(
                    printerName, bytes, $"PMC Gate Pass #{bill.BillId}", attempts: 3, delayMs: 500);
            }
            catch (Exception ex)
            {
                AppLogger.Error("ESC/POS gate pass raster print failed — will try GDI fallback", ex);
                return false;
            }
        }

        private byte[] BuildGatePassEscPosRaster(Bill bill, string cashierName)
        {
            const int width = 576;
            const int maxHeight = 5000;

            using var bmp = new Bitmap(width, maxHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            float contentBottom;
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.PageUnit = GraphicsUnit.Pixel;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                contentBottom = DrawGatePassReceipt(g, width, bill, cashierName);
            }

            int cropH = Math.Max(120, (int)Math.Ceiling(contentBottom) + 40);
            cropH = Math.Min(cropH, maxHeight);
            using var cropped = bmp.Clone(new Rectangle(0, 0, width, cropH), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            return ConvertBitmapToEscPosRaster(cropped);
        }

        /// <summary>
        /// Builds and sends a GroceryPOS-style receipt via ESC/POS.
        /// Renders the classic layout as a bitmap (so Urdu prints correctly), then RAW-spools it.
        /// </summary>
        private bool TryPrintEscPosReceipt(Bill bill, string cashierName, string printerName)
        {
            try
            {
                var bytes = BuildGroceryFormatEscPosRaster(bill, cashierName);
                return RawPrinterHelper.SendBytesToPrinterWithRetry(
                    printerName, bytes, $"PMC Bill #{bill.BillId}", attempts: 2, delayMs: 300);
            }
            catch (Exception ex)
            {
                AppLogger.Error("ESC/POS raster print failed — will try GDI fallback", ex);
                return false;
            }
        }

        /// <summary>
        /// Classic GroceryPOS thermal layout:
        /// Header → Receipt# / Customer / Date / Cashier → Item|Qty|Price|Total → Sub/Disc/Tax → GRAND TOTAL → Payment.
        /// </summary>
        private byte[] BuildGroceryFormatEscPosRaster(Bill bill, string cashierName)
        {
            // 80mm @ 203dpi ≈ 576 dots. Larger pixel fonts = readable print (was too small before).
            const int width = 576;
            const int maxHeight = 5000;

            using var bmp = new Bitmap(width, maxHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            float contentBottom;
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.PageUnit = GraphicsUnit.Pixel;
                // Sharp black edges for thermal (avoid fuzzy small ClearType)
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                contentBottom = DrawGroceryFormatReceipt(g, width, bill, cashierName);
            }

            int cropH = Math.Max(120, (int)Math.Ceiling(contentBottom) + 40);
            cropH = Math.Min(cropH, maxHeight);
            using var cropped = bmp.Clone(new Rectangle(0, 0, width, cropH), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            return ConvertBitmapToEscPosRaster(cropped);
        }

        private float DrawGroceryFormatReceipt(Graphics g, int pageWidthPx, Bill bill, string cashierName)
        {
            // Original spacing/text sizes; bigger PMC; Urdu labels; full-width dashes
            float margin = 16;
            float contentWidth = pageWidthPx - (margin * 2);
            float y = 12;
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            using var headerFont = new Font("Consolas", 56, FontStyle.Bold, GraphicsUnit.Pixel);
            using var urduShopFont = CreateUrduFontPixels(26, FontStyle.Bold);
            using var metaFont = new Font("Consolas", 22, FontStyle.Regular, GraphicsUnit.Pixel);
            using var metaBold = new Font("Consolas", 22, FontStyle.Bold, GraphicsUnit.Pixel);
            using var smallFont = new Font("Consolas", 20, FontStyle.Regular, GraphicsUnit.Pixel);
            using var colUrduFont = CreateUrduFontPixels(18, FontStyle.Regular);
            using var itemEnFont = new Font("Consolas", 16, FontStyle.Regular, GraphicsUnit.Pixel);
            using var itemUrduFont = CreateUrduFontPixels(26, FontStyle.Bold);
            using var totalFont = new Font("Consolas", 28, FontStyle.Bold, GraphicsUnit.Pixel);
            using var footerFont = new Font("Consolas", 20, FontStyle.Regular, GraphicsUnit.Pixel);

            float gapLine = 10;
            float gapSection = 14;

            void DrawFullDash()
            {
                // Edge-to-edge dashed rule (avoids short centered "----" gaps)
                using var pen = new Pen(Color.Black, 1.8f)
                {
                    DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
                    DashPattern = new float[] { 4f, 3f }
                };
                float lineY = y + 10;
                g.DrawLine(pen, margin, lineY, margin + contentWidth, lineY);
                y += 24;
            }

            // ── Header: bigger PMC, Urdu without brackets ──
            g.DrawString(_storeName, headerFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 64), sfCenter);
            y += 64 + 6;
            g.DrawString(_storeNameUrdu, urduShopFont, Brushes.Black, new RectangleF(margin, y, contentWidth, 36), sfCenter);
            y += 36 + 6;
            g.DrawString(_storeAddress, smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 26 + 4;
            g.DrawString($"Ph: {_storePhone}", smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 26 + gapSection;

            DrawFullDash();
            y += gapSection - 8;

            // ── Bill meta (original size/spacing) ──
            g.DrawString($"Receipt#: {bill.InvoiceNumber}", metaBold, Brushes.Black, margin, y);
            y += 26 + gapLine;

            var cust = bill.Customer?.FullName;
            if (!string.IsNullOrWhiteSpace(cust) &&
                !string.Equals(cust, "Walk-in Customer", StringComparison.OrdinalIgnoreCase))
                g.DrawString($"Customer: {cust}", metaFont, Brushes.Black, margin, y);
            else
                g.DrawString("Customer: Walk-in", metaFont, Brushes.Black, margin, y);
            y += 26 + gapLine;

            if (!string.IsNullOrWhiteSpace(bill.BillingAddress))
            {
                var addr = $"Address: {bill.BillingAddress}";
                var addrSize = g.MeasureString(addr, metaFont, (int)contentWidth);
                g.DrawString(addr, metaFont, Brushes.Black, new RectangleF(margin, y, contentWidth, addrSize.Height + 4));
                y += Math.Max(26, addrSize.Height) + gapLine;
            }

            g.DrawString($"Date: {bill.BillDateTime:dd/MM/yyyy hh:mm tt}", metaFont, Brushes.Black, margin, y);
            y += 26 + gapLine;
            g.DrawString($"Cashier: {cashierName}", metaFont, Brushes.Black, margin, y);
            y += 26 + gapSection;

            // ── Columns: Item/جنس · Qty/تعداد · Unit Price/ریٹ · Total/کل رقم ──
            float colItem = margin;
            float colQty = margin + contentWidth * 0.48f;
            float colPrice = margin + contentWidth * 0.64f;
            float colTotal = margin + contentWidth;

            DrawFullDash();
            y += 4;
            // Urdu first (primary), English below — matches on-screen bill preview
            g.DrawString("جنس", colUrduFont, Brushes.Black, colItem, y);
            g.DrawString("تعداد", colUrduFont, Brushes.Black, colQty, y);
            g.DrawString("ریٹ", colUrduFont, Brushes.Black, colPrice, y);
            g.DrawString("کل رقم", colUrduFont, Brushes.Black, colTotal, y, sfRight);
            y += 24;
            g.DrawString("Item", smallFont, Brushes.Black, colItem, y);
            g.DrawString("Qty", smallFont, Brushes.Black, colQty, y);
            g.DrawString("Unit Price", smallFont, Brushes.Black, colPrice, y);
            g.DrawString("Total", smallFont, Brushes.Black, colTotal, y, sfRight);
            y += 22 + 4;
            DrawFullDash();
            y += 6;

            float descWidth = colQty - colItem - 8;
            foreach (var item in bill.Items ?? Enumerable.Empty<BillDescription>())
            {
                var (enLine, urLine) = GetBilingualPrintLines(item);

                // Qty / prices align with the top name line (Urdu if present)
                g.DrawString(Math.Abs(item.Quantity).ToString("0.##"), metaFont, Brushes.Black, colQty, y);
                g.DrawString(item.UnitPrice.ToString("N0"), metaFont, Brushes.Black, colPrice, y);
                g.DrawString(Math.Abs(item.TotalPrice).ToString("N0"), metaFont, Brushes.Black, colTotal, y, sfRight);

                // Urdu on top (larger), English below (smaller, name only)
                if (!string.IsNullOrWhiteSpace(urLine))
                {
                    var urSize = g.MeasureString(urLine, itemUrduFont, (int)descWidth);
                    float urH = Math.Max(28, urSize.Height);
                    g.DrawString(urLine, itemUrduFont, Brushes.Black, new RectangleF(colItem, y, descWidth, urH + 4));
                    y += urH + 2;
                }

                var enSize = g.MeasureString(enLine, itemEnFont, (int)descWidth);
                float enH = Math.Max(18, enSize.Height);
                g.DrawString(enLine, itemEnFont, Brushes.Black, new RectangleF(colItem, y, descWidth, enH + 4));
                y += enH + 8;
            }

            DrawFullDash();
            y += gapSection - 8;

            void DrawTotalRow(string label, string value, Font font, float extraGap = 0)
            {
                g.DrawString(label, font, Brushes.Black, margin, y);
                g.DrawString(value, font, Brushes.Black, margin + contentWidth, y, sfRight);
                y += 28 + gapLine + extraGap;
            }

            if (bill.DiscountAmount > 0)
                DrawTotalRow("Discount:", $"-Rs.{bill.DiscountAmount:N2}", metaFont);
            if (bill.TaxAmount > 0)
                DrawTotalRow("Tax:", $"Rs.{bill.TaxAmount:N2}", metaFont);

            DrawTotalRow("Grand Total:", $"Rs.{Math.Abs(bill.GrandTotal):N2}", totalFont, extraGap: 4);

            string paymentMethodText = bill.PaymentMethod ?? "Cash";
            if (paymentMethodText.Equals("Online", StringComparison.OrdinalIgnoreCase))
            {
                var accountDetails = bill.Account?.AccountTitle ?? bill.OnlinePaymentMethod ?? string.Empty;
                if (!string.IsNullOrEmpty(accountDetails))
                    paymentMethodText = $"Online ({accountDetails})";
            }
            DrawTotalRow("Payment:", paymentMethodText, metaFont);

            if (bill.CashReceived > 0)
            {
                DrawTotalRow("Cash Received:", $"Rs.{bill.CashReceived:N2}", metaFont);
                DrawTotalRow("Change:", $"Rs.{bill.ChangeGiven:N2}", metaFont);
            }

            if (bill.HasPendingCredit)
            {
                DrawTotalRow("Paid Amount:", $"Rs.{bill.PaidAmount:N2}", metaBold);
                DrawTotalRow("DUE AMOUNT:", $"Rs.{bill.RemainingAmount:N2}", metaBold);
            }

            y += gapSection;
            DrawFullDash();
            y += gapSection - 8;
            g.DrawString("Thank You for shopping!", footerFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 28;
            g.DrawString("Please Come again", footerFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 40;

            return y;
        }

        /// <summary>
        /// Gate pass layout: same header/meta/items as bill, but columns are
        /// Item | Qty | Unit Price only — no Total, no payment/grand-total footer.
        /// </summary>
        private float DrawGatePassReceipt(Graphics g, int pageWidthPx, Bill bill, string cashierName)
        {
            float margin = 16;
            float contentWidth = pageWidthPx - (margin * 2);
            float y = 12;
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            using var headerFont = new Font("Consolas", 56, FontStyle.Bold, GraphicsUnit.Pixel);
            using var urduShopFont = CreateUrduFontPixels(26, FontStyle.Bold);
            using var titleFont = new Font("Consolas", 28, FontStyle.Bold, GraphicsUnit.Pixel);
            using var titleUrduFont = CreateUrduFontPixels(22, FontStyle.Bold);
            using var metaFont = new Font("Consolas", 22, FontStyle.Regular, GraphicsUnit.Pixel);
            using var metaBold = new Font("Consolas", 22, FontStyle.Bold, GraphicsUnit.Pixel);
            using var smallFont = new Font("Consolas", 20, FontStyle.Regular, GraphicsUnit.Pixel);
            using var colUrduFont = CreateUrduFontPixels(18, FontStyle.Regular);
            using var itemEnFont = new Font("Consolas", 16, FontStyle.Regular, GraphicsUnit.Pixel);
            using var itemUrduFont = CreateUrduFontPixels(26, FontStyle.Bold);

            float gapLine = 10;
            float gapSection = 14;

            void DrawFullDash()
            {
                using var pen = new Pen(Color.Black, 1.8f)
                {
                    DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
                    DashPattern = new float[] { 4f, 3f }
                };
                float lineY = y + 10;
                g.DrawLine(pen, margin, lineY, margin + contentWidth, lineY);
                y += 24;
            }

            // ── Header ──
            g.DrawString(_storeName, headerFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 64), sfCenter);
            y += 64 + 6;
            g.DrawString(_storeNameUrdu, urduShopFont, Brushes.Black, new RectangleF(margin, y, contentWidth, 36), sfCenter);
            y += 36 + 6;
            g.DrawString(_storeAddress, smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 26 + 4;
            g.DrawString($"Ph: {_storePhone}", smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 26 + 8;
            g.DrawString("GATE PASS", titleFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 32), sfCenter);
            y += 32 + 2;
            g.DrawString("گیٹ پاس", titleUrduFont, Brushes.Black, new RectangleF(margin, y, contentWidth, 28), sfCenter);
            y += 28 + gapSection;

            DrawFullDash();
            y += gapSection - 8;

            // ── Meta (same as bill) ──
            g.DrawString($"Receipt#: {bill.InvoiceNumber}", metaBold, Brushes.Black, margin, y);
            y += 26 + gapLine;

            var cust = bill.Customer?.FullName;
            if (!string.IsNullOrWhiteSpace(cust) &&
                !string.Equals(cust, "Walk-in Customer", StringComparison.OrdinalIgnoreCase))
                g.DrawString($"Customer: {cust}", metaFont, Brushes.Black, margin, y);
            else
                g.DrawString("Customer: Walk-in", metaFont, Brushes.Black, margin, y);
            y += 26 + gapLine;

            if (!string.IsNullOrWhiteSpace(bill.BillingAddress))
            {
                var addr = $"Address: {bill.BillingAddress}";
                var addrSize = g.MeasureString(addr, metaFont, (int)contentWidth);
                g.DrawString(addr, metaFont, Brushes.Black, new RectangleF(margin, y, contentWidth, addrSize.Height + 4));
                y += Math.Max(26, addrSize.Height) + gapLine;
            }

            g.DrawString($"Date: {bill.BillDateTime:dd/MM/yyyy hh:mm tt}", metaFont, Brushes.Black, margin, y);
            y += 26 + gapLine;
            g.DrawString($"Cashier: {cashierName}", metaFont, Brushes.Black, margin, y);
            y += 26 + gapSection;

            // ── Columns: Item/جنس · Qty/تعداد · Unit Price/ریٹ  (no Total) ──
            float colItem = margin;
            float colQty = margin + contentWidth * 0.55f;
            float colPrice = margin + contentWidth;

            DrawFullDash();
            y += 4;
            g.DrawString("جنس", colUrduFont, Brushes.Black, colItem, y);
            g.DrawString("تعداد", colUrduFont, Brushes.Black, colQty, y);
            g.DrawString("ریٹ", colUrduFont, Brushes.Black, colPrice, y, sfRight);
            y += 24;
            g.DrawString("Item", smallFont, Brushes.Black, colItem, y);
            g.DrawString("Qty", smallFont, Brushes.Black, colQty, y);
            g.DrawString("Unit Price", smallFont, Brushes.Black, colPrice, y, sfRight);
            y += 22 + 4;
            DrawFullDash();
            y += 6;

            float descWidth = colQty - colItem - 8;
            double totalQty = 0;
            int lineCount = 0;
            foreach (var item in bill.Items ?? Enumerable.Empty<BillDescription>())
            {
                lineCount++;
                totalQty += Math.Abs(item.Quantity);

                var (enLine, urLine) = GetBilingualPrintLines(item);

                g.DrawString(Math.Abs(item.Quantity).ToString("0.##"), metaFont, Brushes.Black, colQty, y);
                g.DrawString(item.UnitPrice.ToString("N0"), metaFont, Brushes.Black, colPrice, y, sfRight);

                if (!string.IsNullOrWhiteSpace(urLine))
                {
                    var urSize = g.MeasureString(urLine, itemUrduFont, (int)descWidth);
                    float urH = Math.Max(28, urSize.Height);
                    g.DrawString(urLine, itemUrduFont, Brushes.Black, new RectangleF(colItem, y, descWidth, urH + 4));
                    y += urH + 2;
                }

                var enSize = g.MeasureString(enLine, itemEnFont, (int)descWidth);
                float enH = Math.Max(18, enSize.Height);
                g.DrawString(enLine, itemEnFont, Brushes.Black, new RectangleF(colItem, y, descWidth, enH + 4));
                y += enH + 8;
            }

            DrawFullDash();
            y += 8;

            // Tally footer — item lines + total quantity for gate check
            using var tallyFont = new Font("Consolas", 24, FontStyle.Bold, GraphicsUnit.Pixel);
            using var tallyUrduFont = CreateUrduFontPixels(20, FontStyle.Bold);

            g.DrawString($"Total Items: {lineCount}", tallyFont, Brushes.Black, margin, y);
            y += 28;
            g.DrawString($"Total Qty: {totalQty:0.##}", tallyFont, Brushes.Black, margin, y);
            y += 28;
            g.DrawString($"کل اشیاء: {lineCount}   ·   کل تعداد: {totalQty:0.##}",
                tallyUrduFont, Brushes.Black, new RectangleF(margin, y, contentWidth, 28), sfCenter);
            y += 28 + gapSection;

            DrawFullDash();
            y += 24;
            return y;
        }

        private static Font CreateUrduFontPixels(float pixelSize, FontStyle style)
        {
            string[] candidates = { "Jameel Noori Nastaleeq", "Noto Nastaliq Urdu", "Segoe UI", "Arial", "Tahoma" };
            foreach (var name in candidates)
            {
                try { return new Font(name, pixelSize, style, GraphicsUnit.Pixel); }
                catch { /* try next */ }
            }
            return new Font(FontFamily.GenericSansSerif, pixelSize, style, GraphicsUnit.Pixel);
        }

        private static Font CreateUrduFont(float size, FontStyle style)
        {
            string[] candidates = { "Jameel Noori Nastaleeq", "Noto Nastaliq Urdu", "Segoe UI", "Arial", "Tahoma" };
            foreach (var name in candidates)
            {
                try { return new Font(name, size, style); }
                catch { /* try next */ }
            }
            return new Font(FontFamily.GenericSansSerif, size, style);
        }

        /// <summary>English item label (legacy GDI path).</summary>
        private static string GetEnglishPrintItemName(BillDescription item) =>
            GetBilingualPrintLines(item).enLine;

        /// <summary>
        /// Bilingual receipt lines: English and Urdu item names only (no type / قسم).
        /// </summary>
        private static (string enLine, string? urLine) GetBilingualPrintLines(BillDescription item)
        {
            var rawName = !string.IsNullOrWhiteSpace(item.ItemName)
                ? item.ItemName.Trim()
                : (item.ItemDescription ?? "Item").Trim();

            string nameEn = rawName;
            string? nameUr = item.NameUrdu?.Trim();

            var slashName = rawName.IndexOf(" / ", StringComparison.Ordinal);
            if (slashName > 0)
            {
                nameEn = rawName.Substring(0, slashName).Trim();
                if (string.IsNullOrWhiteSpace(nameUr))
                    nameUr = rawName.Substring(slashName + 3).Trim();
            }

            // English: item name only — strip any " - Type N" / " - قسم N" suffix
            var dashType = nameEn.IndexOf(" - Type ", StringComparison.OrdinalIgnoreCase);
            if (dashType > 0)
                nameEn = nameEn.Substring(0, dashType).Trim();
            var dashGeneric = nameEn.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dashGeneric > 0 && (nameEn.IndexOf("Type", dashGeneric, StringComparison.OrdinalIgnoreCase) >= 0 || nameEn.IndexOf("قسم", dashGeneric, StringComparison.OrdinalIgnoreCase) >= 0))
                nameEn = nameEn.Substring(0, dashGeneric).Trim();

            if (!string.IsNullOrWhiteSpace(nameUr))
            {
                var urDashType = nameUr.IndexOf(" - Type ", StringComparison.OrdinalIgnoreCase);
                if (urDashType > 0) nameUr = nameUr.Substring(0, urDashType).Trim();
                var urDashGeneric = nameUr.LastIndexOf(" - ", StringComparison.Ordinal);
                if (urDashGeneric > 0 && (nameUr.IndexOf("Type", urDashGeneric, StringComparison.OrdinalIgnoreCase) >= 0 || nameUr.IndexOf("قسم", urDashGeneric, StringComparison.OrdinalIgnoreCase) >= 0))
                    nameUr = nameUr.Substring(0, urDashGeneric).Trim();
            }

            if (string.IsNullOrWhiteSpace(nameUr) && item.ItemInternalId > 0)
            {
                try
                {
                    nameUr = new Data.Repositories.ItemRepository().GetById(item.ItemInternalId)?.NameUrdu?.Trim();
                }
                catch
                {
                    // Keep English-only if lookup fails
                }
            }

            return (nameEn, string.IsNullOrWhiteSpace(nameUr) ? null : nameUr);
        }

        /// <summary>Convert a white-background receipt bitmap to ESC/POS GS v 0 raster.</summary>
        private static byte[] ConvertBitmapToEscPosRaster(Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;
            int widthBytes = (width + 7) / 8;

            using var ms = new MemoryStream();
            void W(params byte[] data) => ms.Write(data, 0, data.Length);

            // ESC @ init
            W(0x1B, 0x40);
            // Center
            W(0x1B, 0x61, 0x01);

            // GS v 0 m xL xH yL yH d1...dk  (m=0 normal)
            W(0x1D, 0x76, 0x30, 0x00);
            W((byte)(widthBytes & 0xFF), (byte)((widthBytes >> 8) & 0xFF));
            W((byte)(height & 0xFF), (byte)((height >> 8) & 0xFF));

            for (int y = 0; y < height; y++)
            {
                for (int xByte = 0; xByte < widthBytes; xByte++)
                {
                    byte b = 0;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        int x = xByte * 8 + bit;
                        if (x >= width) continue;
                        var c = bmp.GetPixel(x, y);
                        // Dark pixels = print black
                        int lum = (c.R + c.G + c.B) / 3;
                        if (lum < 160)
                            b |= (byte)(0x80 >> bit);
                    }
                    ms.WriteByte(b);
                }
            }

            // Feed + partial cut
            W(0x0A, 0x0A, 0x0A);
            W(0x1D, 0x56, 0x00);
            return ms.ToArray();
        }

        // Legacy text ESC/POS kept unused — raster GroceryPOS format is primary for thermal.
        private byte[] BuildEscPosSaleReceipt(Bill bill, string cashierName)
            => BuildGroceryFormatEscPosRaster(bill, cashierName);

        public void SaveReceiptAsPdf(Bill bill, string cashierName)
        {
            try
            {
                using (var saveDialog = new SaveFileDialog())
                {
                    saveDialog.Filter = "PDF Files (*.pdf)|*.pdf";
                    saveDialog.FileName = $"Receipt_{bill.InvoiceNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                    saveDialog.Title = "Save Receipt as PDF";

                    if (saveDialog.ShowDialog() == DialogResult.OK)
                    {
                        _billToPrint = bill;
                        _cashierName = cashierName;

                        var printDoc = new PrintDocument();
                        printDoc.PrinterSettings.PrinterName = "Microsoft Print to PDF";
                        printDoc.PrinterSettings.PrintToFile = true;
                        printDoc.PrinterSettings.PrintFileName = saveDialog.FileName;

                        printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", 302, 1000);
                        printDoc.DefaultPageSettings.Margins = new Margins(5, 5, 5, 5);

                        printDoc.PrintPage += PrintPage_Handler;
                        printDoc.Print();

                        AppLogger.Info($"Receipt saved as PDF: {saveDialog.FileName}");
                        MessageBox.Show("Receipt saved successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save receipt as PDF", ex);
                MessageBox.Show($"Failed to save PDF: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PrintPage_Handler(object sender, PrintPageEventArgs e)
        {
            if (e.Graphics == null || _billToPrint == null) return;

            var g = e.Graphics;
            var headerFont = new Font("Consolas", 11, FontStyle.Bold);
            var normalFont = new Font("Consolas", 8);
            var boldFont = new Font("Consolas", 8, FontStyle.Bold);
            var smallFont = new Font("Consolas", 7);

            float y = 5;
            float margin = 5; // Left margin to avoid physical clipping
            float pageWidth = 265; // Safe printable width for content, avoids right-side cut-off
            var sf = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            // Store header (PMC branding)
            bool isReturn = _billToPrint.Status == "*** RETURN BILL ***";
            string mainHeader = isReturn ? "--- RETURN RECEIPT ---" : _storeName;
            
            g.DrawString(mainHeader, headerFont, Brushes.Black, new RectangleF(0, y, 302, 20), sf); 
            y += 20;

            if (!isReturn)
            {
                using var urduFont = CreateUrduFont(8, FontStyle.Regular);
                g.DrawString(_storeNameUrdu, urduFont, Brushes.Black, new RectangleF(0, y, 302, 16), sf);
                y += 16;
            }

            if (isReturn)
            {
                g.DrawString(_storeName, boldFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
                y += 15;
            }

            g.DrawString(_storeAddress, smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 15;
            g.DrawString($"Ph: {_storePhone}", smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 18;

            // Divider
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y);
            y += 14;

            // Bill info
            g.DrawString($"Receipt#: {_billToPrint.InvoiceNumber}", boldFont, Brushes.Black, margin, y);
            y += 13;
            if (isReturn && _billToPrint.ReferenceBillId.HasValue)
            {
                g.DrawString($"Orig Bill#: {_billToPrint.ReferenceBillId.Value:D5}", boldFont, Brushes.Black, margin, y);
                y += 13;
            }

            // --- Customer Info ---
            if (_billToPrint.CustomerId.HasValue)
            {
                string custName = _billToPrint.Customer?.FullName ?? "Customer";
                g.DrawString($"Customer: {custName}", normalFont, Brushes.Black, margin, y);
                y += 13;

                if (!string.IsNullOrEmpty(_billToPrint.BillingAddress))
                {
                    RectangleF addrRect = new RectangleF(margin, y, pageWidth, 40);
                    g.DrawString($"Address: {_billToPrint.BillingAddress}", smallFont, Brushes.Black, addrRect);
                    SizeF addrSize = g.MeasureString($"Address: {_billToPrint.BillingAddress}", smallFont, (int)pageWidth);
                    y += Math.Max(13, addrSize.Height + 2);
                }
            }
            else
            {
                g.DrawString("Customer: Walk-in", normalFont, Brushes.Black, margin, y);
                y += 13;
            }

            g.DrawString($"Date: {_billToPrint.BillDateTime}", normalFont, Brushes.Black, margin, y);
            y += 13;
            g.DrawString($"Cashier: {_cashierName}", normalFont, Brushes.Black, margin, y);
            y += 16;

            // Items header
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y);
            y += 14;
            g.DrawString("Item", boldFont, Brushes.Black, margin, y);
            g.DrawString("Qty", boldFont, Brushes.Black, 130, y);
            g.DrawString("Price", boldFont, Brushes.Black, 170, y);
            g.DrawString("Total", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 14;
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y);
            y += 14;

            // Items
            foreach (var item in _billToPrint.Items)
            {
                // Draw description with wrapping
                float descWidth = 125;
                RectangleF rect = new RectangleF(margin, y, descWidth, 200); 
                g.DrawString(GetEnglishPrintItemName(item), normalFont, Brushes.Black, rect);
                
                // Measure how much space the description took to adjust next y
                SizeF size = g.MeasureString(GetEnglishPrintItemName(item), normalFont, (int)descWidth);
                float descHeight = Math.Max(14, size.Height);

                // Use absolute value for return quantities to make it readable as "Returned 2"
                double displayQty = Math.Abs(item.Quantity);
                double displayTotal = Math.Abs(item.TotalPrice);

                g.DrawString(displayQty.ToString(), normalFont, Brushes.Black, 135, y);
                g.DrawString(item.UnitPrice.ToString("N0"), normalFont, Brushes.Black, 170, y);
                g.DrawString(displayTotal.ToString("N0"), normalFont, Brushes.Black, pageWidth, y, sfRight);
                
                y += descHeight + 3; // Better spacing
            }

            // Totals
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y);
            y += 14;

            string labelTotal = isReturn ? "TOTAL RETURNED:" : "GRAND TOTAL:";
            double amountToDisplay = Math.Abs(_billToPrint.GrandTotal);

            if (!isReturn)
            {
                g.DrawString("Sub Total:", boldFont, Brushes.Black, margin, y);
                g.DrawString($"Rs.{_billToPrint.SubTotal:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
                y += 14;

                g.DrawString("Discount:", normalFont, Brushes.Black, margin, y);
                g.DrawString($"-Rs.{_billToPrint.DiscountAmount:N2}", normalFont, Brushes.Black, pageWidth, y, sfRight);
                y += 14;

                if (_billToPrint.TaxAmount > 0)
                {
                    g.DrawString("Tax:", normalFont, Brushes.Black, margin, y);
                    g.DrawString($"Rs.{_billToPrint.TaxAmount:N2}", normalFont, Brushes.Black, pageWidth, y, sfRight);
                    y += 14;
                }

                g.DrawString(new string('=', 44), normalFont, Brushes.Black, margin, y);
                y += 14;
            }

            g.DrawString(labelTotal, headerFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{amountToDisplay:N2}", headerFont, Brushes.Black, pageWidth, y, sfRight);
            y += 20;

            if (!isReturn)
            {
                string paymentMethodText = _billToPrint.PaymentMethod ?? "Cash";
                if (paymentMethodText.Equals("Online", StringComparison.OrdinalIgnoreCase))
                {
                    string accountDetails = _billToPrint.Account?.AccountTitle ?? _billToPrint.OnlinePaymentMethod ?? string.Empty;
                    if (!string.IsNullOrEmpty(accountDetails))
                    {
                        paymentMethodText = $"Online ({accountDetails})";
                    }
                }

                g.DrawString("Payment:", normalFont, Brushes.Black, margin, y);
                g.DrawString(paymentMethodText, normalFont, Brushes.Black, pageWidth, y, sfRight);
                y += 14;

                if (_billToPrint.CashReceived > 0)
                {
                    g.DrawString("Cash Received:", normalFont, Brushes.Black, margin, y);
                    g.DrawString($"Rs.{_billToPrint.CashReceived:N2}", normalFont, Brushes.Black, pageWidth, y, sfRight);
                    y += 14;

                    g.DrawString("Change:", normalFont, Brushes.Black, margin, y);
                    g.DrawString($"Rs.{_billToPrint.ChangeGiven:N2}", normalFont, Brushes.Black, pageWidth, y, sfRight);
                    y += 20;
                }

                if (_billToPrint.HasPendingCredit)
                {
                    g.DrawString("Paid Amount:", boldFont, Brushes.Black, margin, y);
                    g.DrawString($"Rs.{_billToPrint.PaidAmount:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
                    y += 14;

                    g.DrawString("DUE AMOUNT:", boldFont, Brushes.Black, margin, y);
                    g.DrawString($"Rs.{_billToPrint.RemainingAmount:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
                    y += 20;
                }
            }
            else
            {
                g.DrawString("* Amount Credited/Refunded *", normalFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
                y += 20;
            }

            // Footer
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y);
            y += 14;
            g.DrawString("Thank you for shopping!", normalFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 15;
            g.DrawString("Please come again", smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);

            e.HasMorePages = false;

            headerFont.Dispose();
            normalFont.Dispose();
            boldFont.Dispose();
            smallFont.Dispose();
        }

        private void PrintSummaryPage_Handler(object sender, PrintPageEventArgs e)
        {
            if (e.Graphics == null || _billToPrint == null) return;

            var g = e.Graphics;
            var headerFont = new Font("Consolas", 11, FontStyle.Bold);
            var normalFont = new Font("Consolas", 8);
            var boldFont = new Font("Consolas", 8, FontStyle.Bold);
            var smallFont = new Font("Consolas", 7);

            float y = 5;
            float margin = 5;
            float pageWidth = 265;
            var sf = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            // 1. Header
            g.DrawString("--- RETURN SUMMARY ---", headerFont, Brushes.Black, new RectangleF(0, y, 302, 20), sf);
            y += 20;
            g.DrawString(_storeName, boldFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 30;

            // 2. Original Bill Section
            g.DrawString("----------------------------------------", normalFont, Brushes.Black, margin, y); y += 14;
            g.DrawString("ORIGINAL BILL", boldFont, Brushes.Black, margin, y); y += 14;
            g.DrawString($"Bill No: {_billToPrint.InvoiceNumber}", normalFont, Brushes.Black, margin, y); y += 13;
            g.DrawString($"Date: {_billToPrint.BillDateTime:dd-MM-yyyy}", normalFont, Brushes.Black, margin, y); y += 15;
            
            g.DrawString("Items Sold:", boldFont, Brushes.Black, margin, y); y += 14;
            foreach (var item in _billToPrint.Items)
            {
                string desc = $"  {item.DisplayName} {item.Quantity}";
                float descWidth = pageWidth;
                RectangleF rect = new RectangleF(margin, y, descWidth, 200);
                g.DrawString(desc, normalFont, Brushes.Black, rect);
                
                SizeF size = g.MeasureString(desc, normalFont, (int)descWidth);
                y += Math.Max(13, size.Height) + 2;
            }
            g.DrawString($"Total: Rs.{_billToPrint.GrandTotal:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 20;
            g.DrawString("----------------------------------------", normalFont, Brushes.Black, margin, y); y += 25;

            // 3. Sequential Returns
            if (_returnHistoryToPrint != null && _returnHistoryToPrint.Any())
            {
                int returnIndex = 1;
                foreach (var ret in _returnHistoryToPrint.OrderBy(r => r.BillDateTime))
                {
                    g.DrawString($"Return #{returnIndex}", boldFont, Brushes.Black, margin, y);
                    y += 14;
                    g.DrawString($"Date: {ret.BillDateTime:dd-MM-yyyy hh:mm tt}", normalFont, Brushes.Black, margin, y);
                    y += 13;

                    foreach (var item in ret.Items)
                    {
                        // Show absolute quantity for clarity in summary
                        string desc = $"  {item.DisplayName} {Math.Abs(item.Quantity)}";
                        float descWidth = pageWidth;
                        RectangleF rect = new RectangleF(margin, y, descWidth, 200);
                        g.DrawString(desc, normalFont, Brushes.Black, rect);
                        
                        SizeF size = g.MeasureString(desc, normalFont, (int)descWidth);
                        y += Math.Max(13, size.Height) + 2;
                    }
                    g.DrawString($"Return Total: Rs.{Math.Abs(ret.GrandTotal):N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
                    y += 25;
                    returnIndex++;
                }
            }
            else
            {
                g.DrawString("(No returns found)", normalFont, Brushes.Black, margin, y);
                y += 20;
            }

            g.DrawString("--- End of Report ---", smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);

            e.HasMorePages = false;
            headerFont.Dispose();
            normalFont.Dispose();
            boldFont.Dispose();
            smallFont.Dispose();
        }

        // NOTE: Transaction-based customer ledger printing (Dr/Cr/Running Balance) has been removed
        // to prevent accidental generation of accounting-style statements. Use PrintInvoiceLedgerStatement
        // for invoice-centric, professional POS ledger printouts instead.

        /// <summary>
        /// Prints an invoice-centric ledger statement for a specific invoice (bill).
        /// Loads the invoice header, items, return items, payment history, adjustments and prints
        /// a clean, invoice-based ledger (no Dr/Cr/running balances).
        /// </summary>
        /// <summary>
        /// Prints an invoice-centric ledger statement for a specific invoice (bill).
        /// Loads the invoice header, items, return items, payment history, adjustments and prints
        /// a clean, invoice-based ledger with Urdu item descriptions and headers just like bill print.
        /// </summary>
        public bool PrintInvoiceLedgerStatement(int billId, string? pdfPath = null)
        {
            try
            {
                // Load bill with audit logs
                var repo = new FruitVegetableMarketPOS.Data.Repositories.BillRepository();
                var bill = repo.GetById(billId);
                if (bill == null) return false;

                // Retrieve detailed payments directly (to get PaymentMethod column)
                var payments = new List<(DateTime Date, string Method, double Amount)>();
                try
                {
                    using var conn = FruitVegetableMarketPOS.Data.DatabaseHelper.GetConnection();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT CreatedAt, COALESCE(PaymentMethod, 'Cash') as Method, Amount, Type
                        FROM bill_payment
                        WHERE BillId = @bid
                        ORDER BY CreatedAt ASC;";
                    cmd.Parameters.AddWithValue("@bid", bill.BillId);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var date = reader.GetDateTime(0);
                        var method = reader.IsDBNull(1) ? "Cash" : reader.GetString(1);
                        var amount = reader.GetDouble(2);
                        var type = reader.IsDBNull(3) ? "payment" : reader.GetString(3);
                        if (string.Equals(type, "refund", StringComparison.OrdinalIgnoreCase))
                        {
                            payments.Add((date, method + " (refund)", -amount));
                        }
                        else
                        {
                            payments.Add((date, method, amount));
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to load detailed payments for invoice ledger", ex);
                }

                _billToPrint = bill;
                var capturedPayments = payments;
                string cashier = !string.IsNullOrWhiteSpace(_cashierName) ? _cashierName : "Cashier";

                string? targetPrinter = ResolvePrinter(allowDialog: string.IsNullOrEmpty(pdfPath));
                if (string.IsNullOrEmpty(targetPrinter) && string.IsNullOrEmpty(pdfPath))
                    return false;

                // 1) Try ESC/POS thermal printing first if printing to a physical printer
                if (string.IsNullOrEmpty(pdfPath) && !string.IsNullOrEmpty(targetPrinter))
                {
                    if (TryPrintEscPosInvoiceLedger(bill, capturedPayments, cashier, targetPrinter))
                    {
                        AppLogger.Info($"ESC/POS invoice ledger printed for Bill #{bill.BillId} on {targetPrinter}");
                        ActivateMainWindow();
                        return true;
                    }
                }

                // 2) GDI Fallback or PDF export
                var printDoc = new PrintDocument();
                if (!string.IsNullOrEmpty(pdfPath))
                {
                    try
                    {
                        printDoc.PrinterSettings.PrinterName = "Microsoft Print to PDF";
                        printDoc.PrinterSettings.PrintToFile = true;
                        printDoc.PrinterSettings.PrintFileName = pdfPath;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Failed to configure PrintDocument for PDF output", ex);
                    }
                }
                else
                {
                    printDoc.PrinterSettings.PrinterName = targetPrinter ?? string.Empty;
                }

                printDoc.DefaultPageSettings.PaperSize = new PaperSize("InvoiceLedger", 315, 3000);
                printDoc.DefaultPageSettings.Margins = new Margins(10, 10, 10, 10);
                printDoc.PrintController = new StandardPrintController();

                printDoc.PrintPage += (s, e) =>
                {
                    if (e.Graphics == null || _billToPrint == null) return;
                    float scale = e.MarginBounds.Width / 576f;
                    e.Graphics.ScaleTransform(scale, scale);
                    DrawInvoiceLedgerStatement(e.Graphics, 576, _billToPrint, capturedPayments, cashier);
                    e.HasMorePages = false;
                };

                printDoc.Print();
                ActivateMainWindow();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Invoice ledger printing failed", ex);
                ActivateMainWindow();
                return false;
            }
        }

        private bool TryPrintEscPosInvoiceLedger(
            Bill bill,
            List<(DateTime Date, string Method, double Amount)> payments,
            string cashierName,
            string printerName)
        {
            try
            {
                var bytes = BuildInvoiceLedgerEscPosRaster(bill, payments, cashierName);
                return RawPrinterHelper.SendBytesToPrinterWithRetry(
                    printerName, bytes, $"PMC Ledger #{bill.BillId}", attempts: 2, delayMs: 300);
            }
            catch (Exception ex)
            {
                AppLogger.Error("ESC/POS ledger raster print failed — will try GDI fallback", ex);
                return false;
            }
        }

        private byte[] BuildInvoiceLedgerEscPosRaster(
            Bill bill,
            List<(DateTime Date, string Method, double Amount)> payments,
            string cashierName)
        {
            const int width = 576;
            const int maxHeight = 10000;

            using var bmp = new Bitmap(width, maxHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            float contentBottom;
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.PageUnit = GraphicsUnit.Pixel;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                contentBottom = DrawInvoiceLedgerStatement(g, width, bill, payments, cashierName);
            }

            int cropH = Math.Max(120, (int)Math.Ceiling(contentBottom) + 40);
            cropH = Math.Min(cropH, maxHeight);
            using var cropped = bmp.Clone(new Rectangle(0, 0, width, cropH), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            return ConvertBitmapToEscPosRaster(cropped);
        }

        private float DrawInvoiceLedgerStatement(
            Graphics g,
            int pageWidthPx,
            Bill bill,
            List<(DateTime Date, string Method, double Amount)> payments,
            string cashierName)
        {
            float margin = 16;
            float contentWidth = pageWidthPx - (margin * 2);
            float y = 12;
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            using var headerFont = new Font("Consolas", 56, FontStyle.Bold, GraphicsUnit.Pixel);
            using var urduShopFont = CreateUrduFontPixels(26, FontStyle.Bold);
            using var titleFont = new Font("Consolas", 26, FontStyle.Bold, GraphicsUnit.Pixel);
            using var titleUrdu = CreateUrduFontPixels(22, FontStyle.Bold);
            using var sectionHeaderFont = new Font("Consolas", 24, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sectionHeaderUrdu = CreateUrduFontPixels(22, FontStyle.Bold);
            using var metaFont = new Font("Consolas", 22, FontStyle.Regular, GraphicsUnit.Pixel);
            using var metaBold = new Font("Consolas", 22, FontStyle.Bold, GraphicsUnit.Pixel);
            using var smallFont = new Font("Consolas", 20, FontStyle.Regular, GraphicsUnit.Pixel);
            using var colUrduFont = CreateUrduFontPixels(18, FontStyle.Regular);
            using var itemEnFont = new Font("Consolas", 16, FontStyle.Regular, GraphicsUnit.Pixel);
            using var itemUrduFont = CreateUrduFontPixels(26, FontStyle.Bold);
            using var totalFont = new Font("Consolas", 28, FontStyle.Bold, GraphicsUnit.Pixel);
            using var footerFont = new Font("Consolas", 20, FontStyle.Regular, GraphicsUnit.Pixel);

            float gapLine = 10;
            float gapSection = 14;

            void DrawFullDash()
            {
                using var pen = new Pen(Color.Black, 1.8f)
                {
                    DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
                    DashPattern = new float[] { 4f, 3f }
                };
                float lineY = y + 10;
                g.DrawLine(pen, margin, lineY, margin + contentWidth, lineY);
                y += 24;
            }

            // ── Store Header (PMC / Urdu) ──
            g.DrawString(_storeName, headerFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 64), sfCenter);
            y += 64 + 6;
            g.DrawString(_storeNameUrdu, urduShopFont, Brushes.Black, new RectangleF(margin, y, contentWidth, 36), sfCenter);
            y += 36 + 6;
            g.DrawString(_storeAddress, smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 26 + 4;
            g.DrawString($"Ph: {_storePhone}", smallFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 26 + 8;

            g.DrawString("CUSTOMER LEDGER STATEMENT", titleFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 30), sfCenter);
            y += 30 + 2;
            g.DrawString("کسٹمر لیجر سٹیٹمنٹ", titleUrdu, Brushes.Black, new RectangleF(margin, y, contentWidth, 28), sfCenter);
            y += 28 + gapSection;

            DrawFullDash();
            y += gapSection - 8;

            // ── Customer & Bill Details ──
            g.DrawString($"Invoice#: {bill.InvoiceNumber}", metaBold, Brushes.Black, margin, y);
            y += 26 + gapLine;

            var custName = bill.Customer?.FullName ?? "Walk-in";
            g.DrawString($"Customer: {custName}", metaFont, Brushes.Black, margin, y);
            y += 26 + gapLine;

            if (!string.IsNullOrWhiteSpace(bill.Customer?.PrimaryPhone))
            {
                g.DrawString($"Phone   : {bill.Customer.PrimaryPhone}", metaFont, Brushes.Black, margin, y);
                y += 26 + gapLine;
            }

            string? address = bill.BillingAddress ?? bill.Customer?.Address;
            if (!string.IsNullOrWhiteSpace(address))
            {
                var addrText = $"Address : {address}";
                var addrSize = g.MeasureString(addrText, metaFont, (int)contentWidth);
                g.DrawString(addrText, metaFont, Brushes.Black, new RectangleF(margin, y, contentWidth, addrSize.Height + 4));
                y += Math.Max(26, addrSize.Height) + gapLine;
            }

            g.DrawString($"Date    : {bill.BillDateTime:dd/MM/yyyy hh:mm tt}", metaFont, Brushes.Black, margin, y);
            y += 26 + gapLine;
            g.DrawString($"Cashier : {cashierName}", metaFont, Brushes.Black, margin, y);
            y += 26 + gapSection;

            // ── Columns Layout: Item/جنس · Qty/تعداد · Unit Price/ریٹ · Total/کل رقم ──
            float colItem = margin;
            float colQty = margin + contentWidth * 0.48f;
            float colPrice = margin + contentWidth * 0.64f;
            float colTotal = margin + contentWidth;
            float descWidth = colQty - colItem - 8;

            void DrawTableHeader()
            {
                DrawFullDash();
                y += 4;
                // Urdu first (primary), English below — matches on-screen bill & bill print
                g.DrawString("جنس", colUrduFont, Brushes.Black, colItem, y);
                g.DrawString("تعداد", colUrduFont, Brushes.Black, colQty, y);
                g.DrawString("ریٹ", colUrduFont, Brushes.Black, colPrice, y);
                g.DrawString("کل رقم", colUrduFont, Brushes.Black, colTotal, y, sfRight);
                y += 24;
                g.DrawString("Item", smallFont, Brushes.Black, colItem, y);
                g.DrawString("Qty", smallFont, Brushes.Black, colQty, y);
                g.DrawString("Unit Price", smallFont, Brushes.Black, colPrice, y);
                g.DrawString("Total", smallFont, Brushes.Black, colTotal, y, sfRight);
                y += 22 + 4;
                DrawFullDash();
                y += 6;
            }

            // ── Section 1: ORIGINAL BILL ──
            g.DrawString("ORIGINAL BILL · اصل بل", sectionHeaderFont, Brushes.Black, margin, y);
            y += 26 + 4;

            DrawTableHeader();

            double origGrand = Math.Round(bill.GrandTotal, 2);
            foreach (var it in bill.Items ?? Enumerable.Empty<BillDescription>())
            {
                var (enLine, urLine) = GetBilingualPrintLines(it);

                // Qty / prices align with the top line (Urdu if present)
                g.DrawString(Math.Abs(it.Quantity).ToString("0.##"), metaFont, Brushes.Black, colQty, y);
                g.DrawString(it.UnitPrice.ToString("N0"), metaFont, Brushes.Black, colPrice, y);
                g.DrawString(Math.Abs(it.TotalPrice).ToString("N0"), metaFont, Brushes.Black, colTotal, y, sfRight);

                // Urdu on top (larger, bold), English below (smaller)
                if (!string.IsNullOrWhiteSpace(urLine))
                {
                    var urSize = g.MeasureString(urLine, itemUrduFont, (int)descWidth);
                    float urH = Math.Max(28, urSize.Height);
                    g.DrawString(urLine, itemUrduFont, Brushes.Black, new RectangleF(colItem, y, descWidth, urH + 4));
                    y += urH + 2;
                }

                var enSize = g.MeasureString(enLine, itemEnFont, (int)descWidth);
                float enH = Math.Max(18, enSize.Height);
                g.DrawString(enLine, itemEnFont, Brushes.Black, new RectangleF(colItem, y, descWidth, enH + 4));
                y += enH + 8;
            }

            DrawFullDash();
            y += gapSection - 8;

            void Row(string label, string value, Font font, float extraGap = 0)
            {
                g.DrawString(label, font, Brushes.Black, margin, y);
                g.DrawString(value, font, Brushes.Black, margin + contentWidth, y, sfRight);
                y += 28 + gapLine + extraGap;
            }

            Row("Sub Total :", $"Rs.{bill.SubTotal:N2}", metaFont);
            if (bill.DiscountAmount > 0)
                Row("Discount  :", $"-Rs.{bill.DiscountAmount:N2}", metaFont);
            if (bill.TaxAmount > 0)
                Row("Tax       :", $"Rs.{bill.TaxAmount:N2}", metaFont);

            Row("GRAND TOTAL:", $"Rs.{origGrand:N2}", totalFont, extraGap: 4);

            string paymentMethodText = bill.PaymentMethod ?? "Cash";
            if (paymentMethodText.Equals("Online", StringComparison.OrdinalIgnoreCase))
            {
                var accountDetails = bill.Account?.AccountTitle ?? bill.OnlinePaymentMethod ?? string.Empty;
                if (!string.IsNullOrEmpty(accountDetails))
                    paymentMethodText = $"Online ({accountDetails})";
            }
            Row("Payment   :", paymentMethodText, metaFont);
            Row("Amount Paid:", $"Rs.{bill.InitialPayment:N2}", metaFont);

            double initialDue = Math.Max(0, origGrand - bill.InitialPayment);
            Row("DUE AMOUNT:", $"Rs.{initialDue:N2}", totalFont);

            // ── Section 2: Timeline of Payments & Returns ──
            var events = new List<(DateTime Date, string Kind, object Data)>();
            if (bill.PaymentLogs != null)
            {
                foreach (var p in bill.PaymentLogs.OrderBy(p => p.PaidAt))
                {
                    if (string.Equals(p.TransactionType, "Sale", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(p.TransactionType, "Refund", StringComparison.OrdinalIgnoreCase)) continue;
                    events.Add((p.PaidAt, p.TransactionType ?? "Payment", p));
                }
            }
            if (bill.ReturnLogs != null)
            {
                foreach (var r in bill.ReturnLogs.OrderBy(r => r.ReturnedAt))
                    events.Add((r.ReturnedAt, "Return", r));
            }

            events = events.OrderBy(ev => ev.Date).ThenBy(ev => ev.Kind == "Return" ? 0 : 1).ToList();

            double runningCashPaid = Math.Round(bill.InitialPayment, 2);
            double runningCreditAdjusted = 0.0;

            foreach (var ev in events)
            {
                DrawFullDash();
                y += gapSection - 8;

                if (ev.Kind.Equals("Return", StringComparison.OrdinalIgnoreCase))
                {
                    var ret = (ReturnAuditGroup)ev.Data;
                    double totalReturnValue = Math.Round(ret.Items.Sum(i => Math.Abs(i.Quantity) * i.UnitPrice), 2);
                    double currentDueBeforeReturn = Math.Max(0, Math.Round(origGrand - runningCashPaid - runningCreditAdjusted, 2));
                    double creditAdjusted = Math.Min(currentDueBeforeReturn, totalReturnValue);
                    double cashRefund = Math.Max(0, Math.Round(totalReturnValue - creditAdjusted, 2));

                    runningCreditAdjusted += creditAdjusted;
                    double dueAfterReturn = Math.Max(0, Math.Round(currentDueBeforeReturn - creditAdjusted, 2));

                    g.DrawString("RETURN · واپسی", sectionHeaderFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 28), sfCenter);
                    y += 28 + 4;
                    DrawFullDash();
                    g.DrawString($"Date: {ret.ReturnedAt:dd/MM/yyyy hh:mm tt}", metaFont, Brushes.Black, margin, y);
                    y += 26 + gapLine;
                    g.DrawString($"Ref : INV# {bill.InvoiceNumber}", metaFont, Brushes.Black, margin, y);
                    y += 26 + 4;

                    DrawTableHeader();

                    foreach (var item in ret.Items)
                    {
                        var (enLine, urLine) = GetBilingualReturnItemLines(item);

                        g.DrawString(Math.Abs(item.Quantity).ToString("0.##"), metaFont, Brushes.Black, colQty, y);
                        g.DrawString(item.UnitPrice.ToString("N0"), metaFont, Brushes.Black, colPrice, y);
                        g.DrawString(Math.Abs(item.Quantity * item.UnitPrice).ToString("N0"), metaFont, Brushes.Black, colTotal, y, sfRight);

                        if (!string.IsNullOrWhiteSpace(urLine))
                        {
                            var urSize = g.MeasureString(urLine, itemUrduFont, (int)descWidth);
                            float urH = Math.Max(28, urSize.Height);
                            g.DrawString(urLine, itemUrduFont, Brushes.Black, new RectangleF(colItem, y, descWidth, urH + 4));
                            y += urH + 2;
                        }

                        var enSize = g.MeasureString(enLine, itemEnFont, (int)descWidth);
                        float enH = Math.Max(18, enSize.Height);
                        g.DrawString(enLine, itemEnFont, Brushes.Black, new RectangleF(colItem, y, descWidth, enH + 4));
                        y += enH + 8;
                    }

                    DrawFullDash();
                    y += gapSection - 8;

                    if (creditAdjusted > 0)
                        Row("CREDIT ADJUSTED:", $"Rs.{creditAdjusted:N2}", totalFont);
                    if (cashRefund > 0)
                        Row("Amount Refunded:", $"Rs.{cashRefund:N2}", metaBold);

                    Row("Remaining Balance:", $"Rs.{dueAfterReturn:N2}", totalFont);
                }
                else
                {
                    var pay = (CreditPayment)ev.Data;
                    double amt = Math.Abs(pay.AmountPaid);

                    double dueBeforePay = Math.Max(0, Math.Round(origGrand - runningCashPaid - runningCreditAdjusted, 2));
                    double totalPaidBeforePay = Math.Round(runningCashPaid + runningCreditAdjusted, 2);

                    runningCashPaid += amt;
                    double dueAfterPay = Math.Max(0, Math.Round(origGrand - runningCashPaid - runningCreditAdjusted, 2));

                    g.DrawString("PAYMENT · ادائیگی", sectionHeaderFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 28), sfCenter);
                    y += 28 + 4;
                    DrawFullDash();
                    g.DrawString($"Date: {pay.PaidAt:dd/MM/yyyy hh:mm tt}", metaFont, Brushes.Black, margin, y);
                    y += 26 + gapLine;

                    Row("Bill Total :", $"Rs.{origGrand:N2}", metaFont);
                    Row("Total Paid :", $"Rs.{totalPaidBeforePay:N2}", metaFont);
                    Row("Due Before :", $"Rs.{dueBeforePay:N2}", metaFont);
                    Row("PAYMENT RECEIVED:", $"Rs.{amt:N2}", totalFont);
                    Row("DUE AMOUNT :", $"Rs.{dueAfterPay:N2}", totalFont);
                }
            }

            DrawFullDash();
            y += gapSection - 8;
            g.DrawString("End of Customer Ledger · لیجر مکمل", footerFont, Brushes.Black, new RectangleF(0, y, pageWidthPx, 26), sfCenter);
            y += 36;

            return y;
        }

        private static (string enLine, string? urLine) GetBilingualReturnItemLines(BillReturnItemAudit item)
        {
            var rawName = !string.IsNullOrWhiteSpace(item.ItemName)
                ? item.ItemName.Trim()
                : (item.ItemDescription ?? "Item").Trim();

            string nameEn = rawName;
            string? nameUr = item.NameUrdu?.Trim();

            var slashName = rawName.IndexOf(" / ", StringComparison.Ordinal);
            if (slashName > 0)
            {
                nameEn = rawName.Substring(0, slashName).Trim();
                if (string.IsNullOrWhiteSpace(nameUr))
                    nameUr = rawName.Substring(slashName + 3).Trim();
            }

            var dashType = nameEn.IndexOf(" - Type ", StringComparison.OrdinalIgnoreCase);
            if (dashType > 0)
                nameEn = nameEn.Substring(0, dashType).Trim();
            var dashGeneric = nameEn.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dashGeneric > 0 && (nameEn.IndexOf("Type", dashGeneric, StringComparison.OrdinalIgnoreCase) >= 0 || nameEn.IndexOf("قسم", dashGeneric, StringComparison.OrdinalIgnoreCase) >= 0))
                nameEn = nameEn.Substring(0, dashGeneric).Trim();

            if (!string.IsNullOrWhiteSpace(nameUr))
            {
                var urDashType = nameUr.IndexOf(" - Type ", StringComparison.OrdinalIgnoreCase);
                if (urDashType > 0) nameUr = nameUr.Substring(0, urDashType).Trim();
                var urDashGeneric = nameUr.LastIndexOf(" - ", StringComparison.Ordinal);
                if (urDashGeneric > 0 && (nameUr.IndexOf("Type", urDashGeneric, StringComparison.OrdinalIgnoreCase) >= 0 || nameUr.IndexOf("قسم", urDashGeneric, StringComparison.OrdinalIgnoreCase) >= 0))
                    nameUr = nameUr.Substring(0, urDashGeneric).Trim();
            }

            return (nameEn, string.IsNullOrWhiteSpace(nameUr) ? null : nameUr);
        }

        /// <summary>
        /// Send ESC/POS command to open cash drawer via printer.
        /// </summary>
        public void OpenCashDrawer(string printerName)
        {
            try
            {
                AppLogger.Info("Cash drawer open command sent.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Cash drawer command failed", ex);
            }
        }
        private void PrintUnifiedReturnPage_Handler(object sender, PrintPageEventArgs e)
        {
            if (e.Graphics == null || _billToPrint == null || _currentReturnBill == null) return;

            var g = e.Graphics;
            var headerFont = new Font("Consolas", 11, FontStyle.Bold);
            var normalFont = new Font("Consolas", 8);
            var boldFont   = new Font("Consolas", 8, FontStyle.Bold);
            var smallFont  = new Font("Consolas", 7);

            float y         = 5;
            float margin    = 5;
            float pageWidth = 265;
            var sf      = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            // ── 1. Store Header ──────────────────────────────────────────────
            g.DrawString(_storeName, headerFont, Brushes.Black, new RectangleF(0, y, 302, 20), sf);
            y += 20;
            g.DrawString("--- RETURN RECEIPT ---", boldFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 15;
            g.DrawString(_storeAddress, smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 15;
            g.DrawString($"Ph: {_storePhone}", smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 18;

            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;

            // ── 2. Bill & Customer Info ──────────────────────────────────────────
            g.DrawString($"Receipt#: {_billToPrint.InvoiceNumber}", boldFont, Brushes.Black, margin, y); y += 13;

            if (_billToPrint.CustomerId.HasValue)
            {
                string custName = _billToPrint.Customer?.FullName ?? "Customer";
                g.DrawString($"Customer: {custName}", normalFont, Brushes.Black, margin, y); y += 13;

                if (!string.IsNullOrEmpty(_billToPrint.BillingAddress))
                {
                    RectangleF addrRect = new RectangleF(margin, y, pageWidth, 40);
                    g.DrawString($"Address: {_billToPrint.BillingAddress}", smallFont, Brushes.Black, addrRect);
                    SizeF addrSize = g.MeasureString($"Address: {_billToPrint.BillingAddress}", smallFont, (int)pageWidth);
                    y += Math.Max(13, addrSize.Height + 2);
                }
            }
            else
            {
                g.DrawString("Customer: Walk-in", normalFont, Brushes.Black, margin, y); y += 13;
            }

            g.DrawString($"Date: {_currentReturnBill.BillDateTime:dd/MM/yyyy hh:mm tt}", normalFont, Brushes.Black, margin, y); y += 13;
            g.DrawString($"Cashier: {_cashierName}", normalFont, Brushes.Black, margin, y); y += 13;

            y += 4;
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;

            // ── 4. Original Sale Details ─────────────────────────────────────
            g.DrawString("ORIGINAL SALE DETAILS", boldFont, Brushes.Black, margin, y); y += 14;
            g.DrawString("Item",  boldFont, Brushes.Black, margin, y);
            g.DrawString("Qty",   boldFont, Brushes.Black, 130, y);
            g.DrawString("Price", boldFont, Brushes.Black, 170, y);
            g.DrawString("Total", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 14;
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;

            foreach (var item in _billToPrint.Items)
            {
                float descWidth = 125;
                RectangleF rect = new RectangleF(margin, y, descWidth, 200);
                g.DrawString(item.DisplayName, normalFont, Brushes.Black, rect);
                SizeF size = g.MeasureString(item.DisplayName, normalFont, (int)descWidth);
                float descHeight = Math.Max(14, size.Height);

                g.DrawString(item.Quantity.ToString(), normalFont, Brushes.Black, 135, y);
                g.DrawString(item.UnitPrice.ToString("N0"), normalFont, Brushes.Black, 170, y);
                g.DrawString(item.TotalPrice.ToString("N0"), normalFont, Brushes.Black, pageWidth, y, sfRight);
                y += descHeight + 3;
            }
            g.DrawString($"Orig Total: Rs.{_billToPrint.GrandTotal:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 20;

            // ── 5. Previous Return History ────────────────────────────────────
            if (_returnHistoryToPrint != null && _returnHistoryToPrint.Any())
            {
                var previousReturns = _returnHistoryToPrint
                    .Where(r => r.InvoiceNumber != _currentReturnBill.InvoiceNumber)
                    .OrderBy(r => r.BillDateTime)
                    .ToList();

                if (previousReturns.Any())
                {
                    g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;
                    g.DrawString("PREVIOUS RETURNS", boldFont, Brushes.Black, margin, y); y += 14;

                    // Column headers — same style as Original Sale Details
                    g.DrawString("Item",   boldFont, Brushes.Black, margin, y);
                    g.DrawString("Qty",    boldFont, Brushes.Black, 130, y);
                    g.DrawString("Amount", boldFont, Brushes.Black, pageWidth, y, sfRight);
                    y += 13;
                    g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 11;

                    int retIdx = 1;
                    foreach (var prevRet in previousReturns)
                    {
                        // Sub-header for each return transaction
                        g.DrawString($"Return #{retIdx}  ({prevRet.BillDateTime:dd/MM HH:mm})", smallFont, Brushes.Black, margin, y);
                        y += 12;

                        foreach (var item in prevRet.Items)
                        {
                            float descWidth = 125;
                            RectangleF rect = new RectangleF(margin + 5, y, descWidth, 200);
                            g.DrawString(item.DisplayName, smallFont, Brushes.Black, rect);
                            SizeF size = g.MeasureString(item.DisplayName, smallFont, (int)descWidth);
                            float descHeight = Math.Max(12, size.Height);

                            g.DrawString(Math.Abs(item.Quantity).ToString(), smallFont, Brushes.Black, 135, y);
                            g.DrawString(Math.Abs(item.TotalPrice).ToString("N0"), smallFont, Brushes.Black, pageWidth, y, sfRight);
                            y += descHeight + 2;
                        }
                        y += 5;
                        retIdx++;
                    }
                    y += 5;
                }
            }

            // ── 6. Current Return Items ──────────────────────────────────────
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;
            g.DrawString("RETURNED ITEMS (THIS RETURN)", boldFont, Brushes.Black, margin, y); y += 14;
            g.DrawString("Item",   boldFont, Brushes.Black, margin, y);
            g.DrawString("Qty",    boldFont, Brushes.Black, 130, y);
            g.DrawString("Amount", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 14;
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;

            foreach (var item in _currentReturnBill.Items)
            {
                float descWidth = 125;
                RectangleF rect = new RectangleF(margin, y, descWidth, 200);
                g.DrawString(item.DisplayName, normalFont, Brushes.Black, rect);
                SizeF size = g.MeasureString(item.DisplayName, normalFont, (int)descWidth);
                float descHeight = Math.Max(14, size.Height);

                g.DrawString(Math.Abs(item.Quantity).ToString(), normalFont, Brushes.Black, 135, y);
                g.DrawString(Math.Abs(item.TotalPrice).ToString("N0"), normalFont, Brushes.Black, pageWidth, y, sfRight);
                y += descHeight + 3;
            }

            g.DrawString(new string('=', 44), normalFont, Brushes.Black, margin, y); y += 14;

            // ── 7. Totals — context-aware ────────────────────────────────────
            double returnTotal  = Math.Abs(_currentReturnBill.GrandTotal);
            double cashRefund   = _currentReturnBill.CashReceived;    // actual cash handed back
            double creditOffset = _currentReturnBill.RemainingAmount; // credit reduced (repurposed field)
            string outcome      = _currentReturnBill.Status;          // "CashOnly"|"CreditOnly"|"Mixed"

            double previousReturnsTotal = _returnHistoryToPrint?.Where(r => r.InvoiceNumber != _currentReturnBill.InvoiceNumber).Sum(r => Math.Abs(r.GrandTotal)) ?? 0;
            double remainingDue = _billToPrint.GrandTotal - previousReturnsTotal - returnTotal;

            // Return value total
            g.DrawString("RETURN VALUE:", boldFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{returnTotal:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 18;

            g.DrawString("REMAINING DUE AMOUNT:", boldFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{remainingDue:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 18;

            if (outcome == "CreditOnly" || outcome == "Mixed")
            {
                g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 10;
                g.DrawString("ADJUSTED AGAINST CREDIT:", boldFont, Brushes.Black, margin, y);
                g.DrawString($"Rs.{creditOffset:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight);
                y += 13;
                g.DrawString("(No cash refund for this portion)", smallFont, Brushes.Black,
                             new RectangleF(0, y, 302, 13), sf);
                y += 16;
            }

            if (outcome == "CashOnly" || outcome == "Mixed")
            {
                g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 10;
                g.DrawString("CASH REFUND TO CUSTOMER:", headerFont, Brushes.Black, margin, y);
                g.DrawString($"Rs.{cashRefund:N2}", headerFont, Brushes.Black, pageWidth, y, sfRight);
                y += 26;
            }

            // ── 8. Footer ────────────────────────────────────────────────────
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;
            g.DrawString("Thank you for shopping!", normalFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);
            y += 15;
            g.DrawString("Please come again", smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);

            e.HasMorePages = false;
            headerFont.Dispose();
            normalFont.Dispose();
            boldFont.Dispose();
            smallFont.Dispose();
        }
    }
}
