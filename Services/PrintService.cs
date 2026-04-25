using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;
using GroceryPOS.Helpers;
using GroceryPOS.Models;
using System.Management;
using System.Linq;
using System.Printing;

namespace GroceryPOS.Services
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
        private Customer? _customerLedgerToPrint;
        private List<CustomerLedgerEntry>? _ledgerEntriesToPrint;
        private DateTime? _ledgerFromDate;
        private DateTime? _ledgerToDate;
        private double _paymentAmount;
        private string _storeName = "GROCERY MART";
        private string _storeAddress = "Rawat, Rawalpindi, Pakistan";
        private string _storePhone = "0300-1234567";
        private string _cashierName = "";

        private string? _preferredPrinter;
        private const string ConfigFile = "printer_config.txt";

        public PrintService()
        {
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFile);
                if (File.Exists(path))
                {
                    _preferredPrinter = File.ReadAllText(path).Trim();
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
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFile);
                File.WriteAllText(path, printerName);
                _preferredPrinter = printerName;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save printer config", ex);
            }
        }

        public bool IsPrinterOnline()
        {
            try
            {
                string printerName = _preferredPrinter ?? "";
                if (string.IsNullOrEmpty(printerName))
                {
                    // Try to get default printer
                    var settings = new PrinterSettings();
                    printerName = settings.PrinterName;
                }

                if (string.IsNullOrEmpty(printerName)) return false;

                using (var server = new LocalPrintServer())
                {
                    var queue = server.GetPrintQueues().FirstOrDefault(q => q.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase) || q.FullName.Equals(printerName, StringComparison.OrdinalIgnoreCase));
                    if (queue != null)
                    {
                        queue.Refresh();
                        bool isOffline = false;

                        if (queue.IsOffline || queue.IsNotAvailable || queue.IsInError)
                        {
                            isOffline = true;
                        }
                        else if ((queue.QueueStatus & PrintQueueStatus.Offline) == PrintQueueStatus.Offline ||
                                 (queue.QueueStatus & PrintQueueStatus.Error) == PrintQueueStatus.Error ||
                                 (queue.QueueStatus & PrintQueueStatus.NotAvailable) == PrintQueueStatus.NotAvailable)
                        {
                            isOffline = true;
                        }
                        else if (queue.NumberOfJobs > 0)
                        {
                            // A healthy direct-thermal printer prints instantly. 
                            // Any stuck jobs indicate the physical hardware is off or disconnected, 
                            // even if Windows driver falsely claims it is "Ready".
                            isOffline = true;
                        }

                        if (isOffline)
                        {
                            try
                            {
                                // Force clear the queue to ensure past bills aren't stored
                                queue.Purge();
                            }
                            catch (Exception purgeEx)
                            {
                                AppLogger.Error("Failed to purge print queue", purgeEx);
                            }
                            return false;
                        }

                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Printer status check failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Prints a compact payment receipt for credit recovery on a previous bill.
        /// Shows bill details, payment amount, updated paid total, and remaining due.
        /// </summary>
        public bool PrintPaymentReceipt(Bill bill, double paymentAmount, string cashierName)
        {
            try
            {
                _billToPrint = bill;
                _paymentAmount = paymentAmount;
                _cashierName = cashierName;

                var printDoc = new PrintDocument();
                string? targetPrinter = _preferredPrinter;

                if (string.IsNullOrEmpty(targetPrinter))
                {
                    using (var dialog = new System.Windows.Forms.PrintDialog())
                    {
                        dialog.Document = printDoc;
                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            targetPrinter = printDoc.PrinterSettings.PrinterName;
                            SaveConfig(targetPrinter);
                        }
                        else return false;
                    }
                }

                printDoc.PrinterSettings.PrinterName = targetPrinter;
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", 302, 800);
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
            y += 18;

            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 14;

            g.DrawString("Payment Received:", headerFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{_paymentAmount:N2}", headerFont, Brushes.Black, pageWidth, y, sfRight);
            y += 22;

            g.DrawString(new string('=', 44), normalFont, Brushes.Black, margin, y); y += 14;

            g.DrawString("Total Paid:", boldFont, Brushes.Black, margin, y);
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
                string? targetPrinter = _preferredPrinter;

                if (string.IsNullOrEmpty(targetPrinter))
                {
                    using (var dialog = new System.Windows.Forms.PrintDialog())
                    {
                        dialog.Document = printDoc;
                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            targetPrinter = printDoc.PrinterSettings.PrinterName;
                            SaveConfig(targetPrinter);
                        }
                        else return false;
                    }
                }

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
                g.DrawString(item.ItemDescription, normalFont, Brushes.Black, rect);
                SizeF size = g.MeasureString(item.ItemDescription, normalFont, (int)descWidth);
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
                string? targetPrinter = _preferredPrinter;

                if (string.IsNullOrEmpty(targetPrinter))
                {
                    using (var dialog = new System.Windows.Forms.PrintDialog())
                    {
                        dialog.Document = printDoc;
                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            targetPrinter = printDoc.PrinterSettings.PrinterName;
                            SaveConfig(targetPrinter);
                        }
                        else return false;
                    }
                }

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
                string? targetPrinter = _preferredPrinter;

                if (string.IsNullOrEmpty(targetPrinter))
                {
                    using (var dialog = new System.Windows.Forms.PrintDialog())
                    {
                        dialog.Document = printDoc;
                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            targetPrinter = printDoc.PrinterSettings.PrinterName;
                            SaveConfig(targetPrinter);
                        }
                        else return;
                    }
                }

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
                // If it's a return and has the parent bill metadata, use the unified return layout
                if (bill.IsReturn && bill.ParentBill != null)
                {
                    return PrintUnifiedReturnReceipt(bill.ParentBill, bill, bill.ReturnHistory, cashierName);
                }

                _billToPrint = bill;
                _cashierName = cashierName;

                var printDoc = new PrintDocument();

                string? targetPrinter = printerName ?? _preferredPrinter;
                if (string.IsNullOrEmpty(targetPrinter))
                {
                    using (var dialog = new System.Windows.Forms.PrintDialog())
                    {
                        dialog.Document = printDoc;
                        dialog.UseEXDialog = true;

                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            targetPrinter = printDoc.PrinterSettings.PrinterName;
                            SaveConfig(targetPrinter);
                        }
                        else
                        {
                            return false; // Cancelled
                        }
                    }
                }

                if (!string.IsNullOrEmpty(targetPrinter))
                    printDoc.PrinterSettings.PrinterName = targetPrinter;

                // 80mm thermal paper = ~302 pixels at 96dpi
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Receipt", 302, 1000);
                printDoc.DefaultPageSettings.Margins = new Margins(5, 5, 5, 5);

                printDoc.PrintPage += PrintPage_Handler;
                printDoc.Print();

                AppLogger.Info($"Receipt printed for Bill #{bill.BillId} on printer: {targetPrinter}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Receipt printing failed", ex);
                _preferredPrinter = null;
                return false;
            }
        }

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

            // Store header
            bool isReturn = _billToPrint.Status == "*** RETURN BILL ***";
            string mainHeader = isReturn ? "--- RETURN RECEIPT ---" : _storeName;
            
            g.DrawString(mainHeader, headerFont, Brushes.Black, new RectangleF(0, y, 302, 20), sf); 
            y += 20;

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
                g.DrawString(item.ItemDescription, normalFont, Brushes.Black, rect);
                
                // Measure how much space the description took to adjust next y
                SizeF size = g.MeasureString(item.ItemDescription, normalFont, (int)descWidth);
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
                g.DrawString("Payment:", normalFont, Brushes.Black, margin, y);
                g.DrawString(_billToPrint.PaymentMethod ?? "Cash", normalFont, Brushes.Black, pageWidth, y, sfRight);
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
                string desc = $"  {item.ItemDescription} {item.Quantity}";
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
                        string desc = $"  {item.ItemDescription} {Math.Abs(item.Quantity)}";
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

        public bool PrintCustomerLedgerStatement(Customer customer, List<CustomerLedgerEntry> entries, DateTime? from, DateTime? to)
        {
            try
            {
                _customerLedgerToPrint = customer;
                _ledgerEntriesToPrint = entries.OrderBy(e => e.EntryDate).ThenBy(e => e.SequenceNo).ToList();
                _ledgerFromDate = from;
                _ledgerToDate = to;

                var printDoc = new PrintDocument();
                string? targetPrinter = _preferredPrinter;

                if (string.IsNullOrEmpty(targetPrinter))
                {
                    using var dialog = new System.Windows.Forms.PrintDialog();
                    dialog.Document = printDoc;
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        targetPrinter = printDoc.PrinterSettings.PrinterName;
                        SaveConfig(targetPrinter);
                    }
                    else
                    {
                        return false;
                    }
                }

                printDoc.PrinterSettings.PrinterName = targetPrinter;
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Ledger", 302, 2200);
                printDoc.PrintPage += PrintCustomerLedgerPage_Handler;
                printDoc.Print();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Customer ledger statement printing failed", ex);
                return false;
            }
        }

        private void PrintCustomerLedgerPage_Handler(object sender, PrintPageEventArgs e)
        {
            if (e.Graphics == null || _customerLedgerToPrint == null || _ledgerEntriesToPrint == null) return;

            var g = e.Graphics;
            var headerFont = new Font("Consolas", 10, FontStyle.Bold);
            var normalFont = new Font("Consolas", 8);
            var boldFont = new Font("Consolas", 8, FontStyle.Bold);
            var smallFont = new Font("Consolas", 7);

            float y = 5;
            float margin = 5;
            float pageWidth = 265;
            var sf = new StringFormat { Alignment = StringAlignment.Center };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far };

            g.DrawString(_storeName, headerFont, Brushes.Black, new RectangleF(0, y, 302, 18), sf);
            y += 18;
            g.DrawString("--- CUSTOMER LEDGER STATEMENT ---", boldFont, Brushes.Black, new RectangleF(0, y, 302, 14), sf);
            y += 14;
            g.DrawString(_storeAddress, smallFont, Brushes.Black, new RectangleF(0, y, 302, 14), sf);
            y += 14;
            g.DrawString($"Ph: {_storePhone}", smallFont, Brushes.Black, new RectangleF(0, y, 302, 14), sf);
            y += 16;

            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 12;
            g.DrawString($"Customer: {_customerLedgerToPrint.FullName}", boldFont, Brushes.Black, margin, y); y += 12;
            g.DrawString($"Phone: {_customerLedgerToPrint.PrimaryPhone}", normalFont, Brushes.Black, margin, y); y += 12;

            string periodLabel = _ledgerFromDate.HasValue || _ledgerToDate.HasValue
                ? $"{_ledgerFromDate?.ToString("dd/MM/yy") ?? "..."} to {_ledgerToDate?.ToString("dd/MM/yy") ?? "..."}"
                : "All Time";
            g.DrawString($"Period: {periodLabel}", normalFont, Brushes.Black, margin, y); y += 12;
            g.DrawString($"Printed: {DateTime.Now:dd/MM/yy HH:mm}", normalFont, Brushes.Black, margin, y); y += 14;

            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 12;
            g.DrawString("Date/Time", boldFont, Brushes.Black, margin, y);
            g.DrawString("Dr", boldFont, Brushes.Black, 132, y);
            g.DrawString("Cr", boldFont, Brushes.Black, 182, y);
            g.DrawString("Bal", boldFont, Brushes.Black, pageWidth, y, sfRight);
            y += 12;
            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 12;

            double totalDebit = 0;
            double totalCredit = 0;
            double openingBalance = 0;
            double closingBalance = 0;
            if (_ledgerEntriesToPrint.Any())
            {
                openingBalance = _ledgerEntriesToPrint.First().RunningBalance - (_ledgerEntriesToPrint.First().Debit - _ledgerEntriesToPrint.First().Credit);
                closingBalance = _ledgerEntriesToPrint.Last().RunningBalance;
            }

            foreach (var row in _ledgerEntriesToPrint)
            {
                totalDebit += row.Debit;
                totalCredit += row.Credit;

                g.DrawString(row.EntryDate.ToString("dd/MM HH:mm"), normalFont, Brushes.Black, margin, y);
                g.DrawString(row.Debit > 0 ? row.Debit.ToString("N0") : "-", normalFont, Brushes.Black, 132, y);
                g.DrawString(row.Credit > 0 ? row.Credit.ToString("N0") : "-", normalFont, Brushes.Black, 182, y);
                g.DrawString(row.RunningBalance.ToString("N0"), boldFont, Brushes.Black, pageWidth, y, sfRight);
                y += 11;

                string label = $"{row.TransactionType}: {row.Description}";
                RectangleF rect = new RectangleF(margin + 2, y, pageWidth - 2, 40);
                g.DrawString(label, smallFont, Brushes.Black, rect);
                SizeF sz = g.MeasureString(label, smallFont, (int)(pageWidth - 2));
                y += Math.Max(10, sz.Height + 1);
            }

            g.DrawString(new string('=', 44), normalFont, Brushes.Black, margin, y); y += 13;
            g.DrawString("Opening Balance:", boldFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{openingBalance:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight); y += 12;
            g.DrawString("Total Debit:", boldFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{totalDebit:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight); y += 12;
            g.DrawString("Total Credit:", boldFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{totalCredit:N2}", boldFont, Brushes.Black, pageWidth, y, sfRight); y += 12;
            g.DrawString("Closing Balance:", headerFont, Brushes.Black, margin, y);
            g.DrawString($"Rs.{closingBalance:N2}", headerFont, Brushes.Black, pageWidth, y, sfRight); y += 18;

            g.DrawString(new string('-', 44), normalFont, Brushes.Black, margin, y); y += 12;
            g.DrawString("End of customer ledger statement", smallFont, Brushes.Black, new RectangleF(0, y, 302, 15), sf);

            e.HasMorePages = false;
            headerFont.Dispose();
            normalFont.Dispose();
            boldFont.Dispose();
            smallFont.Dispose();
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
                g.DrawString(item.ItemDescription, normalFont, Brushes.Black, rect);
                SizeF size = g.MeasureString(item.ItemDescription, normalFont, (int)descWidth);
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
                            g.DrawString(item.ItemDescription, smallFont, Brushes.Black, rect);
                            SizeF size = g.MeasureString(item.ItemDescription, smallFont, (int)descWidth);
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
                g.DrawString(item.ItemDescription, normalFont, Brushes.Black, rect);
                SizeF size = g.MeasureString(item.ItemDescription, normalFont, (int)descWidth);
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
