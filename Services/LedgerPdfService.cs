using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Services
{
    public sealed class LedgerPdfRow
    {
        public DateTime CreatedAt { get; set; }
        public string InvoiceDisplay { get; set; } = "";
        public string SubtotalDisplay { get; set; } = "—";
        public double PreviousCredit { get; set; }
        public double TotalBanam { get; set; }
        public double ReceivedAmount { get; set; }
        public double PendingCredit { get; set; }
        public bool HasPendingCredit => PendingCredit > 0.01;
        public bool IsPayment { get; set; }
        public bool IsOpening { get; set; }
    }

    /// <summary>
    /// A4 customer-ledger PDF/print: shop header, customer details, summary, then
    /// every bill and recovery in date/time order. Pages are rendered as images so Urdu prints correctly.
    /// </summary>
    public static class LedgerPdfService
    {
        private const string StoreName = "PMC — Pak Madinah Commission Agents";
        private const string StoreNameUrdu = "پاک مدینہ کمیشن ایجنٹس";
        private const string StorePhone = "0345 5113044";
        private const string StoreAddress = "I-11/4 Islamabad";

        private static readonly Color Green = Color.FromArgb(27, 67, 50);
        private static readonly Color PaidGreen = Color.FromArgb(22, 163, 74);
        private static readonly Color Danger = Color.FromArgb(220, 38, 38);
        private static readonly Color RowAlt = Color.FromArgb(248, 250, 252);
        private static readonly Color PayRow = Color.FromArgb(232, 245, 233);
        private static readonly Color Line = Color.FromArgb(226, 232, 240);
        private static readonly Color Mute = Color.FromArgb(100, 116, 139);

        private const int Dpi = 150;
        private const int PageW = 1240;
        private const int PageH = 1754;
        private const int Margin = 48;
        private const float RowH = 32f;
        private const float HeaderRowH = 44f;

        public static void Save(
            string filePath,
            Customer customer,
            IReadOnlyList<LedgerPdfRow> rows,
            double totalPurchased,
            double totalPaid,
            double totalPending)
        {
            var pages = RenderPages(customer, rows, totalPurchased, totalPaid, totalPending);
            try
            {
                WritePdf(filePath, pages);
            }
            finally
            {
                foreach (var p in pages)
                    p.Dispose();
            }
        }

        public static bool Print(
            Customer customer,
            IReadOnlyList<LedgerPdfRow> rows,
            double totalPurchased,
            double totalPaid,
            double totalPending)
        {
            var pages = RenderPages(customer, rows, totalPurchased, totalPaid, totalPending);
            if (pages.Count == 0)
                return false;

            try
            {
                using var printDoc = new PrintDocument();
                printDoc.DocumentName = $"PMC Ledger — {customer.FullName}";
                printDoc.DefaultPageSettings.Landscape = false;
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169);
                printDoc.DefaultPageSettings.Margins = new Margins(20, 20, 20, 20);

                int index = 0;
                printDoc.PrintPage += (_, e) =>
                {
                    if (e.Graphics == null || index >= pages.Count)
                    {
                        e.HasMorePages = false;
                        return;
                    }

                    e.Graphics.DrawImage(pages[index], e.MarginBounds);
                    index++;
                    e.HasMorePages = index < pages.Count;
                };

                using var dlg = new PrintDialog();
                dlg.Document = printDoc;
                dlg.UseEXDialog = true;
                if (dlg.ShowDialog() != DialogResult.OK)
                    return false;

                printDoc.Print();
                return true;
            }
            finally
            {
                foreach (var p in pages)
                    p.Dispose();
            }
        }

        private static List<Bitmap> RenderPages(
            Customer customer,
            IReadOnlyList<LedgerPdfRow> rows,
            double totalPurchased,
            double totalPaid,
            double totalPending)
        {
            var ordered = rows
                .OrderBy(r => r.CreatedAt)
                .ThenBy(r => r.InvoiceDisplay, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pages = new List<Bitmap>();
            int start = 0;
            int pageNo = 1;
            while (true)
            {
                var bmp = new Bitmap(PageW, PageH, PixelFormat.Format24bppRgb);
                bmp.SetResolution(Dpi, Dpi);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    g.Clear(Color.White);
                    var next = DrawPage(g, customer, ordered, start, pageNo,
                        totalPurchased, totalPaid, totalPending, out var more);
                    start = next;
                    pageNo++;
                    pages.Add(bmp);
                    if (!more) break;
                }
            }

            return pages;
        }

        private static int DrawPage(
            Graphics g,
            Customer customer,
            List<LedgerPdfRow> rows,
            int startIndex,
            int pageNo,
            double totalPurchased,
            double totalPaid,
            double totalPending,
            out bool hasMore)
        {
            float y = Margin;
            var contentW = PageW - Margin * 2;

            using var titleFont = new Font("Segoe UI", 32, FontStyle.Bold, GraphicsUnit.Pixel);
            using var subFont = new Font("Segoe UI", 16, FontStyle.Regular, GraphicsUnit.Pixel);
            using var bodyFont = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
            using var bodyBold = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
            using var smallFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            using var kpiFont = new Font("Segoe UI", 24, FontStyle.Bold, GraphicsUnit.Pixel);
            using var colFont = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            using var urduFont = CreateUrdu(20, FontStyle.Bold);
            using var urduSmall = CreateUrdu(14, FontStyle.Bold);
            using var white = new SolidBrush(Color.White);
            using var green = new SolidBrush(Green);
            using var mute = new SolidBrush(Mute);
            using var black = new SolidBrush(Color.FromArgb(27, 41, 53));
            var sfLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            if (pageNo == 1)
            {
                using (var header = new SolidBrush(Green))
                    g.FillRectangle(header, Margin, y, contentW, 132);
                g.DrawString(StoreName, titleFont, white, new RectangleF(Margin + 22, y + 12, contentW - 44, 38), sfLeft);
                g.DrawString(StoreNameUrdu, urduFont, white, new RectangleF(Margin + 22, y + 50, contentW - 44, 28), sfLeft);
                g.DrawString($"Ph: {StorePhone}    ·    {StoreAddress}", subFont, white, new RectangleF(Margin + 22, y + 82, contentW - 44, 22), sfLeft);
                g.DrawString("Customer Ledger", subFont, white, new RectangleF(Margin + 22, y + 104, contentW - 44, 22), sfLeft);
                y += 144;
                g.DrawString($"Generated {DateTime.Now:dd MMM yyyy  HH:mm}", smallFont, mute, Margin, y);
                y += 22;

                g.DrawString("Customer Details", bodyBold, black, Margin, y);
                y += 20;
                using (var box = new SolidBrush(Color.FromArgb(241, 245, 249)))
                    g.FillRectangle(box, Margin, y, contentW, 78);
                using (var pen = new Pen(Line))
                    g.DrawRectangle(pen, Margin, y, contentW, 78);
                float cy = y + 8;
                DrawKv(g, "Name", customer.FullName, Margin + 16, cy, bodyBold, bodyFont, black, mute);
                cy += 22;
                DrawKv(g, "Phone", string.IsNullOrWhiteSpace(customer.PrimaryPhone) ? "—" : customer.PrimaryPhone, Margin + 16, cy, bodyBold, bodyFont, black, mute);
                cy += 22;
                DrawKv(g, "Address", CustomerAddress(customer), Margin + 16, cy, bodyBold, bodyFont, black, mute);
                y += 92;

                using (var bar = new SolidBrush(Green))
                    g.FillRectangle(bar, Margin, y, contentW, 78);
                float colW = contentW / 3f;
                DrawKpi(g, Margin, y, colW, "کل بنام", "Total Purchased amount", $"Rs. {totalPurchased:N0}", Color.White, urduSmall, smallFont, kpiFont, sfCenter);
                DrawKpi(g, Margin + colW, y, colW, "وصولی", "Total paid amount", $"Rs. {totalPaid:N0}", Color.FromArgb(134, 239, 172), urduSmall, smallFont, kpiFont, sfCenter);
                DrawKpi(g, Margin + colW * 2, y, colW, "بقیہ رقم", "Total Pending Amount", $"Rs. {totalPending:N0}", Color.FromArgb(254, 202, 202), urduSmall, smallFont, kpiFont, sfCenter);
                y += 90;
            }
            else
            {
                using (var header = new SolidBrush(Green))
                    g.FillRectangle(header, Margin, y, contentW, 48);
                g.DrawString($"{StoreName}  ·  Ledger continued  ·  {customer.FullName}", bodyBold, white, new RectangleF(Margin + 16, y, contentW - 32, 48), sfLeft);
                y += 62;
            }

            float[] cols = ColumnWidths(contentW);
            string[] en = { "DateTime", "Invoice", "Subtotal", "Previous Credit", "Total", "Received", "Pending Credit", "Type" };
            string[] ur = { "تاریخ و وقت", "بل نمبر", "ذیلی کل", "سابقہ بنام", "کل بنام", "وصول شدہ رقم", "بقیہ رقم", "قسم" };

            using (var hdr = new SolidBrush(Green))
                g.FillRectangle(hdr, Margin, y, contentW, HeaderRowH);
            float x = Margin;
            for (int i = 0; i < cols.Length; i++)
            {
                g.DrawString(en[i], colFont, white, new RectangleF(x + 4, y + 2, cols[i] - 8, 20), i >= 2 && i <= 6 ? sfRight : sfCenter);
                g.DrawString(ur[i], urduSmall, white, new RectangleF(x + 4, y + 20, cols[i] - 8, 22), sfCenter);
                x += cols[i];
            }
            y += HeaderRowH;

            var footerY = PageH - Margin - 22;
            int index = startIndex;
            while (index < rows.Count && y + RowH <= footerY - 8)
            {
                var row = rows[index];
                var bg = row.IsPayment ? PayRow : (index % 2 == 0 ? Color.White : RowAlt);
                using (var brush = new SolidBrush(bg))
                    g.FillRectangle(brush, Margin, y, contentW, RowH);
                using (var pen = new Pen(Line))
                    g.DrawLine(pen, Margin, y + RowH, Margin + contentW, y + RowH);

                string[] vals =
                {
                    row.CreatedAt.ToString("dd/MM/yy HH:mm"),
                    row.InvoiceDisplay,
                    row.SubtotalDisplay,
                    $"Rs. {row.PreviousCredit:N0}",
                    $"Rs. {row.TotalBanam:N0}",
                    $"Rs. {row.ReceivedAmount:N0}",
                    $"Rs. {row.PendingCredit:N0}",
                    row.IsPayment ? "Payment" : (row.IsOpening ? "Opening" : "Bill")
                };
                x = Margin;
                for (int i = 0; i < cols.Length; i++)
                {
                    var brush = black;
                    var font = i == 1 ? bodyBold : bodyFont;
                    if (i == 5) brush = new SolidBrush(PaidGreen);
                    else if (i == 6 && row.HasPendingCredit) brush = new SolidBrush(Danger);
                    else if (i == 7 && row.IsPayment) brush = new SolidBrush(PaidGreen);
                    var align = i >= 2 && i <= 6 ? sfRight : sfCenter;
                    g.DrawString(vals[i], font, brush, new RectangleF(x + 4, y, cols[i] - 8, RowH), align);
                    if (brush != black) brush.Dispose();
                    x += cols[i];
                }
                y += RowH;
                index++;
            }

            hasMore = index < rows.Count;
            g.DrawString($"Generated {DateTime.Now:dd MMM yyyy  HH:mm}", smallFont, mute, Margin, footerY);
            g.DrawString($"Page {pageNo}", smallFont, mute, new RectangleF(Margin, footerY, contentW, 18), sfRight);
            return index;
        }

        private static float[] ColumnWidths(float contentW)
        {
            float[] raw = { 130, 90, 120, 145, 125, 130, 145, 90 };
            var sum = raw.Sum();
            return raw.Select(w => w / sum * contentW).ToArray();
        }

        private static void DrawKv(Graphics g, string label, string value, float x, float y, Font bold, Font body, Brush black, Brush mute)
        {
            g.DrawString(label + "  ", bold, mute, x, y);
            var lw = g.MeasureString(label + "  ", bold).Width;
            g.DrawString(value, body, black, x + lw, y);
        }

        private static void DrawKpi(Graphics g, float x, float y, float w, string urdu, string en, string value, Color valueColor,
            Font urduFont, Font small, Font kpi, StringFormat center)
        {
            using var white = new SolidBrush(Color.White);
            using var val = new SolidBrush(valueColor);
            g.DrawString(urdu, urduFont, white, new RectangleF(x, y + 6, w, 20), center);
            g.DrawString(en, small, white, new RectangleF(x, y + 26, w, 16), center);
            g.DrawString(value, kpi, val, new RectangleF(x, y + 44, w, 28), center);
        }

        private static string CustomerAddress(Customer c)
        {
            var parts = new[] { c.Address, c.Address2, c.Address3 }.Where(s => !string.IsNullOrWhiteSpace(s));
            var text = string.Join(", ", parts!);
            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }

        private static Font CreateUrdu(float px, FontStyle style)
        {
            foreach (var name in new[] { "Jameel Noori Nastaleeq", "Noto Nastaliq Urdu", "Segoe UI", "Arial" })
            {
                try { return new Font(name, px, style, GraphicsUnit.Pixel); }
                catch { /* next */ }
            }
            return new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel);
        }

        private static void WritePdf(string path, List<Bitmap> pages)
        {
            var jpegs = new List<byte[]>();
            foreach (var page in pages)
            {
                using var ms = new MemoryStream();
                var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var enc = new EncoderParameters(1);
                enc.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                page.Save(ms, encoder, enc);
                jpegs.Add(ms.ToArray());
            }

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var w = new StreamWriter(fs, new UTF8Encoding(false), 1024, leaveOpen: true) { NewLine = "\n" };
            var offsets = new List<long> { 0 };
            void ObjStart()
            {
                w.Flush();
                offsets.Add(fs.Position);
            }

            w.Write("%PDF-1.4\n");
            ObjStart();
            w.Write("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");

            int nextId = 3;
            var kids = new StringBuilder();
            for (int i = 0; i < jpegs.Count; i++)
            {
                int pageId = nextId++;
                nextId += 2;
                if (i > 0) kids.Append(' ');
                kids.Append(pageId).Append(" 0 R");
            }

            ObjStart();
            w.Write($"2 0 obj << /Type /Pages /Kids [{kids}] /Count {jpegs.Count} >> endobj\n");

            nextId = 3;
            for (int i = 0; i < jpegs.Count; i++)
            {
                int pageId = nextId++;
                int imgId = nextId++;
                int contentId = nextId++;
                var jpeg = jpegs[i];
                var imgName = $"Im{i + 1}";
                ObjStart();
                w.Write($"{pageId} 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /{imgName} {imgId} 0 R >> >> /Contents {contentId} 0 R >> endobj\n");
                ObjStart();
                w.Write($"{imgId} 0 obj << /Type /XObject /Subtype /Image /Width {PageW} /Height {PageH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
                w.Flush();
                fs.Write(jpeg, 0, jpeg.Length);
                w.Write("\nendstream\nendobj\n");
                var content = $"q 595 0 0 842 0 0 cm /{imgName} Do Q\n";
                var bytes = Encoding.ASCII.GetBytes(content);
                ObjStart();
                w.Write($"{contentId} 0 obj << /Length {bytes.Length} >> stream\n{content}endstream\nendobj\n");
            }

            w.Flush();
            long xref = fs.Position;
            w.Write($"xref\n0 {offsets.Count}\n");
            w.Write("0000000000 65535 f \n");
            for (int i = 1; i < offsets.Count; i++)
                w.Write($"{offsets[i]:D10} 00000 n \n");
            w.Write($"trailer << /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        }
    }
}
