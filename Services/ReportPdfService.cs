using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace FruitVegetableMarketPOS.Services
{
    public sealed class ReportPdfModel
    {
        public string Title { get; set; } = "Report";
        public string Period { get; set; } = "";
        public string Filters { get; set; } = "";
        public List<(string Label, string Value)> Kpis { get; set; } = new();
        public string[] Headers { get; set; } = Array.Empty<string>();
        public List<string[]> Rows { get; set; } = new();
    }

    /// <summary>
    /// A4 image PDF for the active Reports tab. Same shop header as the ledger PDF
    /// so Urdu labels stay readable.
    /// </summary>
    public static class ReportPdfService
    {
        private const string StoreName = "PMC — Pak Madinah Commission Agents";
        private const string StoreNameUrdu = "پاک مدینہ کمیشن ایجنٹس";
        private const string StorePhone = "0345 5113044";
        private const string StoreAddress = "I-11/4 Islamabad";

        private static readonly Color Green = Color.FromArgb(27, 67, 50);
        private static readonly Color Line = Color.FromArgb(226, 232, 240);
        private static readonly Color Mute = Color.FromArgb(100, 116, 139);
        private static readonly Color RowAlt = Color.FromArgb(248, 250, 252);
        private static readonly Color HeadBg = Color.FromArgb(27, 67, 50);

        private const int Dpi = 150;
        private const int PageW = 1240;
        private const int PageH = 1754;
        private const int Margin = 42;
        private const float RowH = 28f;
        private const float HeaderRowH = 32f;

        public static void Save(string filePath, ReportPdfModel model)
        {
            var pages = new List<Bitmap>();
            try
            {
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
                        start = DrawPage(g, model, start, pageNo, out var more);
                        pageNo++;
                        pages.Add(bmp);
                        if (!more) break;
                    }
                }

                WritePdf(filePath, pages);
            }
            finally
            {
                foreach (var p in pages)
                    p.Dispose();
            }
        }

        private static int DrawPage(Graphics g, ReportPdfModel model, int startIndex, int pageNo, out bool hasMore)
        {
            float y = Margin;
            var contentW = PageW - Margin * 2;
            using var titleFont = new Font("Segoe UI", 28, FontStyle.Bold, GraphicsUnit.Pixel);
            using var subFont = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
            using var bodyFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            using var bodyBold = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            using var smallFont = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
            using var urduFont = CreateUrdu(18, FontStyle.Bold);
            using var white = new SolidBrush(Color.White);
            using var mute = new SolidBrush(Mute);
            using var black = new SolidBrush(Color.FromArgb(27, 41, 53));
            using var green = new SolidBrush(Green);
            var sfLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
            var sfRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };

            using (var header = new SolidBrush(Green))
                g.FillRectangle(header, Margin, y, contentW, 108);
            g.DrawString(StoreName, titleFont, white, new RectangleF(Margin + 18, y + 10, contentW - 36, 34), sfLeft);
            g.DrawString(StoreNameUrdu, urduFont, white, new RectangleF(Margin + 18, y + 44, contentW - 36, 26), sfLeft);
            g.DrawString($"Ph: {StorePhone}    ·    {StoreAddress}", subFont, white, new RectangleF(Margin + 18, y + 74, contentW - 36, 22), sfLeft);
            y += 122;

            g.DrawString(model.Title, bodyBold, black, Margin, y);
            y += 18;
            g.DrawString($"{model.Period}    ·    Generated {DateTime.Now:dd MMM yyyy  HH:mm}", smallFont, mute, Margin, y);
            y += 16;
            var extraFilters = model.Filters?.Trim() ?? "";
            if (extraFilters.StartsWith(model.Period, StringComparison.OrdinalIgnoreCase))
                extraFilters = extraFilters[model.Period.Length..].Trim(' ', '·', '-');
            if (!string.IsNullOrWhiteSpace(extraFilters))
            {
                g.DrawString(extraFilters, smallFont, mute, new RectangleF(Margin, y, contentW, 16), sfLeft);
                y += 18;
            }

            if (pageNo == 1 && model.Kpis.Count > 0)
            {
                int cols = Math.Min(4, model.Kpis.Count);
                float boxH = 52;
                float gap = 8;
                float boxW = (contentW - gap * (cols - 1)) / cols;
                for (int i = 0; i < model.Kpis.Count; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    float x = Margin + col * (boxW + gap);
                    float by = y + row * (boxH + gap);
                    using var box = new SolidBrush(Color.FromArgb(241, 245, 249));
                    g.FillRectangle(box, x, by, boxW, boxH);
                    using var pen = new Pen(Line);
                    g.DrawRectangle(pen, x, by, boxW, boxH);
                    g.DrawString(model.Kpis[i].Label, smallFont, mute, new RectangleF(x + 10, by + 6, boxW - 20, 16), sfLeft);
                    g.DrawString(model.Kpis[i].Value, bodyBold, black, new RectangleF(x + 10, by + 24, boxW - 20, 20), sfLeft);
                }
                int kpiRows = (model.Kpis.Count + cols - 1) / cols;
                y += kpiRows * (boxH + gap) + 8;
            }

            int colCount = Math.Max(1, model.Headers.Length);
            var widths = MeasureColumnWidths(g, bodyBold, bodyFont, model.Headers, model.Rows, contentW);
            var rightAlign = new bool[colCount];
            for (int i = 0; i < colCount; i++)
                rightAlign[i] = IsNumericHeader(model.Headers[i]);

            using (var hb = new SolidBrush(HeadBg))
                g.FillRectangle(hb, Margin, y, contentW, HeaderRowH);
            float hx = Margin;
            for (int i = 0; i < colCount; i++)
            {
                var align = rightAlign[i] ? sfRight : sfLeft;
                g.DrawString(model.Headers[i], bodyBold, white, new RectangleF(hx + 8, y, widths[i] - 16, HeaderRowH), align);
                hx += widths[i];
            }
            y += HeaderRowH;

            int index = startIndex;
            while (index < model.Rows.Count)
            {
                if (y + RowH > PageH - Margin - 28)
                    break;
                if ((index - startIndex) % 2 == 1)
                {
                    using var alt = new SolidBrush(RowAlt);
                    g.FillRectangle(alt, Margin, y, contentW, RowH);
                }
                float cx = Margin;
                var cells = model.Rows[index];
                for (int i = 0; i < colCount; i++)
                {
                    var text = i < cells.Length ? cells[i] ?? "" : "";
                    var align = rightAlign[i] ? sfRight : sfLeft;
                    g.DrawString(text, bodyFont, black, new RectangleF(cx + 8, y, widths[i] - 16, RowH), align);
                    cx += widths[i];
                }
                using (var pen = new Pen(Line))
                    g.DrawLine(pen, Margin, y + RowH, Margin + contentW, y + RowH);
                y += RowH;
                index++;
            }

            hasMore = index < model.Rows.Count;
            g.DrawString($"Page {pageNo}", smallFont, mute, new RectangleF(Margin, PageH - Margin + 4, contentW, 18), sfCenter);
            return index;
        }

        private static float[] MeasureColumnWidths(Graphics g, Font headerFont, Font bodyFont,
            string[] headers, List<string[]> rows, float contentW)
        {
            int n = Math.Max(1, headers.Length);
            var widths = new float[n];
            const float pad = 20f;
            for (int i = 0; i < n; i++)
            {
                float max = g.MeasureString(headers[i] ?? "", headerFont).Width;
                foreach (var row in rows)
                {
                    if (i < row.Length)
                        max = Math.Max(max, g.MeasureString(row[i] ?? "", bodyFont).Width);
                }
                widths[i] = Math.Max(56, max + pad);
            }

            float total = 0;
            for (int i = 0; i < n; i++) total += widths[i];
            if (total <= 0) total = contentW;
            if (Math.Abs(total - contentW) > 0.5f)
            {
                float scale = contentW / total;
                for (int i = 0; i < n; i++)
                    widths[i] *= scale;
            }
            return widths;
        }

        private static bool IsNumericHeader(string? header)
        {
            var h = (header ?? "").Replace(" ", "").ToLowerInvariant();
            return h.Contains("amount") || h.Contains("revenue") || h.Contains("received")
                || h.Contains("transaction") || h.Contains("quantity") || h.Contains("bills")
                || h.Contains("credit") || h.Contains("subtotal") || h.Contains("discount")
                || h.Contains("total") || h.Contains("net") || h.Contains("pending")
                || h.Contains("purchases") || h.Contains("lines");
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
