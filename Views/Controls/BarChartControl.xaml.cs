using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Brushes = System.Windows.Media.Brushes;
using FruitVegetableMarketPOS.Models;

using Ellipse = System.Windows.Shapes.Ellipse;
using Path = System.Windows.Shapes.Path;

namespace FruitVegetableMarketPOS.Views.Controls
{
    public enum ReportChartKind { Bar, Line, Donut, Horizontal }
    /// <summary>
    /// Compact WPF bar chart. Skips zero labels, thins x-axis ticks when dense,
    /// and can render horizontal ranking bars for top-item charts.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public partial class BarChartControl : UserControl
    {
        public static readonly DependencyProperty DataSourceProperty =
            DependencyProperty.Register(nameof(DataSource), typeof(IEnumerable), typeof(BarChartControl),
                new PropertyMetadata(null, OnDataSourceChanged));

        public static readonly DependencyProperty ChartTitleProperty =
            DependencyProperty.Register(nameof(ChartTitle), typeof(string), typeof(BarChartControl),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty BarColorProperty =
            DependencyProperty.Register(nameof(BarColor), typeof(Color), typeof(BarChartControl),
                new PropertyMetadata(Color.FromRgb(27, 67, 50)));

        public static readonly DependencyProperty ShowSecondaryBarProperty =
            DependencyProperty.Register(nameof(ShowSecondaryBar), typeof(bool), typeof(BarChartControl),
                new PropertyMetadata(false, OnDataSourceChanged));

        public static readonly DependencyProperty IsHorizontalProperty =
            DependencyProperty.Register(nameof(IsHorizontal), typeof(bool), typeof(BarChartControl),
                new PropertyMetadata(false, OnDataSourceChanged));

        public IEnumerable? DataSource
        {
            get => (IEnumerable?)GetValue(DataSourceProperty);
            set => SetValue(DataSourceProperty, value);
        }

        public string ChartTitle
        {
            get => (string)GetValue(ChartTitleProperty);
            set => SetValue(ChartTitleProperty, value);
        }

        public Color BarColor
        {
            get => (Color)GetValue(BarColorProperty);
            set => SetValue(BarColorProperty, value);
        }

        public bool ShowSecondaryBar
        {
            get => (bool)GetValue(ShowSecondaryBarProperty);
            set => SetValue(ShowSecondaryBarProperty, value);
        }

        public bool IsHorizontal
        {
            get => (bool)GetValue(IsHorizontalProperty);
            set => SetValue(IsHorizontalProperty, value);
        }

        public static readonly DependencyProperty ChartKindProperty =
            DependencyProperty.Register(nameof(ChartKind), typeof(ReportChartKind), typeof(BarChartControl),
                new PropertyMetadata(ReportChartKind.Bar, OnDataSourceChanged));

        public ReportChartKind ChartKind
        {
            get => (ReportChartKind)GetValue(ChartKindProperty);
            set => SetValue(ChartKindProperty, value);
        }

        public BarChartControl()
        {
            InitializeComponent();
            Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(Render), DispatcherPriority.Loaded);
            SizeChanged += (_, _) => Render();
        }

        private static void OnDataSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (BarChartControl)d;

            if (e.OldValue is INotifyCollectionChanged oldCol)
                oldCol.CollectionChanged -= ctrl.OnCollectionChanged;

            if (e.NewValue is INotifyCollectionChanged newCol)
                newCol.CollectionChanged += ctrl.OnCollectionChanged;

            ctrl.Render();
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Render();

        private double PlotWidth() =>
            ActualWidth > 20 ? ActualWidth : Math.Max(ChartCanvas.ActualWidth, 280);

        private double PlotHeight() =>
            ActualHeight > 20 ? ActualHeight : Math.Max(ChartCanvas.ActualHeight, 180);

        private void Render()
        {
            ChartCanvas.Children.Clear();

            var points = DataSource?.OfType<ChartDataPoint>().ToList() ?? new List<ChartDataPoint>();
            if (points.Count == 0)
            {
                EmptyLabel.Visibility = Visibility.Visible;
                return;
            }

            EmptyLabel.Visibility = Visibility.Collapsed;

            var kind = ChartKind == ReportChartKind.Bar && IsHorizontal ? ReportChartKind.Horizontal : ChartKind;
            switch (kind)
            {
                case ReportChartKind.Horizontal:
                    RenderHorizontal(points);
                    break;
                case ReportChartKind.Line:
                    RenderLine(points);
                    break;
                case ReportChartKind.Donut:
                    RenderDonut(points);
                    break;
                default:
                    RenderVertical(points);
                    break;
            }
        }

        private void RenderVertical(List<ChartDataPoint> points)
        {
            double canvasW = PlotWidth();
            double canvasH = PlotHeight();
            const double paddingLeft = 48;
            const double paddingRight = 12;
            const double paddingTop = 22;
            const double paddingBottom = 36;

            double plotW = Math.Max(40, canvasW - paddingLeft - paddingRight);
            double plotH = Math.Max(40, canvasH - paddingTop - paddingBottom);
            double maxVal = Math.Max(1, points.Max(p => p.Value));
            maxVal *= 1.12;

            DrawGrid(paddingLeft, paddingRight, paddingTop, plotH, canvasW, maxVal);

            int n = points.Count;
            double barGroupW = plotW / n;
            double barW = Math.Min(Math.Max(barGroupW * 0.55, 6), 48);
            int nonZero = points.Count(p => p.Value > 0.009);
            bool showValues = nonZero <= 12;
            int labelStep = n <= 10 ? 1 : (int)Math.Ceiling(n / 8.0);

            var fill = new LinearGradientBrush(
                BarColor, Color.FromArgb(210, BarColor.R, BarColor.G, BarColor.B),
                new Point(0, 0), new Point(0, 1));
            var valueBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            var xBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));

            for (int i = 0; i < n; i++)
            {
                var pt = points[i];
                double groupX = paddingLeft + i * barGroupW;
                bool hasValue = pt.Value > 0.009;
                double barH = hasValue ? Math.Max(3, (pt.Value / maxVal) * plotH) : 0;
                double barX = groupX + (barGroupW - barW) / 2;
                double barY = paddingTop + plotH - barH;

                if (hasValue)
                {
                    var bar = new Rectangle
                    {
                        Width = barW,
                        Height = barH,
                        Fill = fill,
                        RadiusX = Math.Min(4, barW / 3),
                        RadiusY = Math.Min(4, barW / 3),
                        ToolTip = $"{pt.Label}  ·  {pt.DisplayValue}"
                    };
                    Canvas.SetLeft(bar, barX);
                    Canvas.SetTop(bar, barY);
                    ChartCanvas.Children.Add(bar);
                }

                if (showValues && hasValue)
                {
                    var valLabel = new TextBlock
                    {
                        Text = pt.DisplayValue,
                        FontSize = n > 10 ? 10 : 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = valueBrush,
                        TextAlignment = TextAlignment.Center,
                        Width = Math.Max(barGroupW - 2, 32)
                    };
                    valLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(valLabel, groupX);
                    Canvas.SetTop(valLabel, Math.Max(2, barY - valLabel.DesiredSize.Height - 1));
                    ChartCanvas.Children.Add(valLabel);
                }

                if (i % labelStep == 0 || i == n - 1)
                {
                    var xLabel = new TextBlock
                    {
                        Text = CompactLabel(pt.Label, barGroupW),
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = xBrush,
                        TextAlignment = TextAlignment.Center,
                        Width = Math.Max(barGroupW * labelStep - 4, 28),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Canvas.SetLeft(xLabel, groupX);
                    Canvas.SetTop(xLabel, paddingTop + plotH + 6);
                    ChartCanvas.Children.Add(xLabel);
                }
            }
        }

        private void RenderHorizontal(List<ChartDataPoint> points)
        {
            double canvasW = PlotWidth();
            double canvasH = PlotHeight();
            const double paddingLeft = 108;
            const double paddingRight = 72;
            const double paddingTop = 8;
            const double paddingBottom = 8;

            double plotW = Math.Max(40, canvasW - paddingLeft - paddingRight);
            double plotH = Math.Max(40, canvasH - paddingTop - paddingBottom);
            int n = points.Count;
            double rowH = plotH / n;
            double barH = Math.Min(22, Math.Max(10, rowH * 0.48));
            double maxVal = Math.Max(1, points.Max(p => p.Value));

            var fill = new LinearGradientBrush(
                Color.FromArgb(255, BarColor.R, BarColor.G, BarColor.B),
                Color.FromArgb(160, BarColor.R, BarColor.G, BarColor.B),
                new Point(0, 0), new Point(1, 0));
            var nameBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            var valueBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            var trackBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249));

            for (int i = 0; i < n; i++)
            {
                var pt = points[i];
                double y = paddingTop + i * rowH + (rowH - barH) / 2;
                double barW = Math.Max(4, (pt.Value / maxVal) * plotW);

                var name = new TextBlock
                {
                    Text = pt.Label ?? "",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = nameBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Width = paddingLeft - 10,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(name, 0);
                Canvas.SetTop(name, y + Math.Max(0, (barH - 16) / 2));
                ChartCanvas.Children.Add(name);

                var track = new Rectangle
                {
                    Width = plotW,
                    Height = barH,
                    Fill = trackBrush,
                    RadiusX = 5,
                    RadiusY = 5
                };
                Canvas.SetLeft(track, paddingLeft);
                Canvas.SetTop(track, y);
                ChartCanvas.Children.Add(track);

                var bar = new Rectangle
                {
                    Width = barW,
                    Height = barH,
                    Fill = fill,
                    RadiusX = 5,
                    RadiusY = 5,
                    ToolTip = $"{pt.Label}  ·  {pt.DisplayValue}"
                };
                Canvas.SetLeft(bar, paddingLeft);
                Canvas.SetTop(bar, y);
                ChartCanvas.Children.Add(bar);

                var val = new TextBlock
                {
                    Text = pt.DisplayValue,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = valueBrush
                };
                Canvas.SetLeft(val, paddingLeft + barW + 8);
                Canvas.SetTop(val, y + Math.Max(0, (barH - 16) / 2));
                ChartCanvas.Children.Add(val);
            }
        }

        private void RenderLine(List<ChartDataPoint> points)
        {
            double canvasW = PlotWidth();
            double canvasH = PlotHeight();
            const double paddingLeft = 48;
            const double paddingRight = 14;
            const double paddingTop = 18;
            const double paddingBottom = 32;
            double plotW = Math.Max(40, canvasW - paddingLeft - paddingRight);
            double plotH = Math.Max(40, canvasH - paddingTop - paddingBottom);
            double maxVal = Math.Max(1, points.Max(p => p.Value)) * 1.12;
            DrawGrid(paddingLeft, paddingRight, paddingTop, plotH, canvasW, maxVal);

            int n = points.Count;
            double step = n <= 1 ? plotW / 2 : plotW / (n - 1);
            var coords = new Point[n];
            for (int i = 0; i < n; i++)
            {
                double x = paddingLeft + (n <= 1 ? plotW / 2 : i * step);
                double y = paddingTop + plotH - (points[i].Value / maxVal) * plotH;
                coords[i] = new Point(x, y);
            }

            if (n > 1)
            {
                var area = new StreamGeometry();
                using (var ctx = area.Open())
                {
                    ctx.BeginFigure(new Point(coords[0].X, paddingTop + plotH), true, true);
                    ctx.LineTo(coords[0], true, false);
                    for (int i = 1; i < n; i++)
                        ctx.LineTo(coords[i], true, false);
                    ctx.LineTo(new Point(coords[n - 1].X, paddingTop + plotH), true, false);
                }
                ChartCanvas.Children.Add(new Path
                {
                    Data = area,
                    Fill = new SolidColorBrush(Color.FromArgb((byte)40, BarColor.R, BarColor.G, BarColor.B))
                });

                ChartCanvas.Children.Add(new Polyline
                {
                    Stroke = new SolidColorBrush(BarColor),
                    StrokeThickness = 2.4,
                    StrokeLineJoin = PenLineJoin.Round,
                    Points = new PointCollection(coords)
                });
            }

            int labelStep = n <= 8 ? 1 : (int)Math.Ceiling(n / 7.0);
            var xBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            for (int i = 0; i < n; i++)
            {
                var dot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = Brushes.White,
                    Stroke = new SolidColorBrush(BarColor),
                    StrokeThickness = 2,
                    ToolTip = $"{points[i].Label}  ·  {points[i].DisplayValue}"
                };
                Canvas.SetLeft(dot, coords[i].X - 3.5);
                Canvas.SetTop(dot, coords[i].Y - 3.5);
                ChartCanvas.Children.Add(dot);

                if (i % labelStep == 0 || i == n - 1)
                {
                    var xLabel = new TextBlock
                    {
                        Text = CompactLabel(points[i].Label, step),
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = xBrush,
                        TextAlignment = TextAlignment.Center,
                        Width = 56
                    };
                    Canvas.SetLeft(xLabel, coords[i].X - 28);
                    Canvas.SetTop(xLabel, paddingTop + plotH + 6);
                    ChartCanvas.Children.Add(xLabel);
                }
            }
        }

        private void RenderDonut(List<ChartDataPoint> points)
        {
            points = points.Where(p => p.Value > 0.009).ToList();
            double canvasW = PlotWidth();
            double canvasH = PlotHeight();
            double total = points.Sum(p => p.Value);
            if (total <= 0.009 || points.Count == 0)
            {
                EmptyLabel.Visibility = Visibility.Visible;
                return;
            }

            const double legendRowH = 26;
            double legendH = Math.Max(28, legendRowH);
            double size = Math.Min(canvasW - 24, canvasH - legendH - 28);
            size = Math.Max(96, Math.Min(size, 176));
            double blockH = size + 18 + legendH;
            double top = Math.Max(4, (canvasH - blockH) / 2);
            double cx = canvasW / 2;
            double cy = top + size / 2;
            double outer = size / 2 - 4;
            double inner = outer * 0.56;

            var palette = new[]
            {
                Color.FromRgb(27, 67, 50),
                Color.FromRgb(14, 165, 233),
                Color.FromRgb(194, 65, 12),
                Color.FromRgb(15, 118, 110),
                Color.FromRgb(124, 58, 237)
            };

            double angle = -90;
            for (int i = 0; i < points.Count; i++)
            {
                double sweep = points[i].Value / total * 360;
                var fill = new SolidColorBrush(palette[i % palette.Length]);
                var tip = $"{points[i].Label}  ·  {points[i].DisplayValue}";
                foreach (var slice in BuildDonutSlices(cx, cy, outer, inner, angle, sweep))
                {
                    slice.Fill = fill;
                    slice.ToolTip = tip;
                    ChartCanvas.Children.Add(slice);
                }
                angle += sweep;
            }

            var holeLabel = new TextBlock
            {
                Text = total >= 100000 ? $"Rs.{total / 1000:N0}K" : $"Rs.{total:N0}",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(27, 67, 50)),
                TextAlignment = TextAlignment.Center,
                Width = Math.Max(52, inner * 2 - 10)
            };
            holeLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(holeLabel, cx - holeLabel.Width / 2);
            Canvas.SetTop(holeLabel, cy - holeLabel.DesiredSize.Height / 2);
            ChartCanvas.Children.Add(holeLabel);

            var legendItems = new List<(Rectangle Swatch, TextBlock Label, double Width)>();
            for (int i = 0; i < points.Count; i++)
            {
                var swatch = new Rectangle
                {
                    Width = 12,
                    Height = 12,
                    RadiusX = 3,
                    RadiusY = 3,
                    Fill = new SolidColorBrush(palette[i % palette.Length])
                };
                var pct = total > 0 ? points[i].Value / total * 100 : 0;
                var label = new TextBlock
                {
                    Text = $"{points[i].Label}  {points[i].DisplayValue}  ({pct:0}%)",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
                };
                label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                legendItems.Add((swatch, label, 12 + 8 + label.DesiredSize.Width));
            }

            const double gap = 22;
            double legendY = cy + outer + 16;
            double totalW = legendItems.Sum(x => x.Width) + gap * Math.Max(0, legendItems.Count - 1);
            bool horizontal = totalW <= canvasW - 16 && legendItems.Count <= 4;

            if (horizontal)
            {
                double x = Math.Max(8, (canvasW - totalW) / 2);
                foreach (var item in legendItems)
                {
                    Canvas.SetLeft(item.Swatch, x);
                    Canvas.SetTop(item.Swatch, legendY + 3);
                    ChartCanvas.Children.Add(item.Swatch);
                    Canvas.SetLeft(item.Label, x + 18);
                    Canvas.SetTop(item.Label, legendY);
                    ChartCanvas.Children.Add(item.Label);
                    x += item.Width + gap;
                }
            }
            else
            {
                double maxW = legendItems.Max(x => x.Width);
                double x = Math.Max(8, (canvasW - maxW) / 2);
                for (int i = 0; i < legendItems.Count; i++)
                {
                    var item = legendItems[i];
                    Canvas.SetLeft(item.Swatch, x);
                    Canvas.SetTop(item.Swatch, legendY + i * legendRowH + 4);
                    ChartCanvas.Children.Add(item.Swatch);
                    Canvas.SetLeft(item.Label, x + 18);
                    Canvas.SetTop(item.Label, legendY + i * legendRowH);
                    ChartCanvas.Children.Add(item.Label);
                }
            }
        }

        private static IEnumerable<Path> BuildDonutSlices(double cx, double cy, double outer, double inner, double startDeg, double sweepDeg)
        {
            if (sweepDeg < 0.3)
                yield break;

            // WPF ArcSegment cannot draw a full 360° ring (start == end). Split it.
            if (sweepDeg >= 359.5)
            {
                yield return BuildDonutSlice(cx, cy, outer, inner, startDeg, 180);
                yield return BuildDonutSlice(cx, cy, outer, inner, startDeg + 180, 180);
                yield break;
            }

            yield return BuildDonutSlice(cx, cy, outer, inner, startDeg, sweepDeg);
        }

        private static Path BuildDonutSlice(double cx, double cy, double outer, double inner, double startDeg, double sweepDeg)
        {
            double ToRad(double d) => d * Math.PI / 180.0;
            Point Pt(double r, double deg) => new(cx + r * Math.Cos(ToRad(deg)), cy + r * Math.Sin(ToRad(deg)));

            bool large = sweepDeg > 180;
            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = Pt(outer, startDeg), IsClosed = true };
            fig.Segments.Add(new ArcSegment
            {
                Point = Pt(outer, startDeg + sweepDeg),
                Size = new System.Windows.Size(outer, outer),
                IsLargeArc = large,
                SweepDirection = SweepDirection.Clockwise
            });
            fig.Segments.Add(new LineSegment { Point = Pt(inner, startDeg + sweepDeg) });
            fig.Segments.Add(new ArcSegment
            {
                Point = Pt(inner, startDeg),
                Size = new System.Windows.Size(inner, inner),
                IsLargeArc = large,
                SweepDirection = SweepDirection.Counterclockwise
            });
            geo.Figures.Add(fig);
            return new Path { Data = geo };
        }

        private void DrawGrid(double paddingLeft, double paddingRight, double paddingTop,
            double plotH, double canvasW, double maxVal)
        {
            int gridLines = 4;
            for (int i = 0; i <= gridLines; i++)
            {
                double yFrac = (double)i / gridLines;
                double yPos = paddingTop + plotH - yFrac * plotH;
                double yVal = yFrac * (maxVal / 1.12);

                ChartCanvas.Children.Add(new Line
                {
                    X1 = paddingLeft,
                    X2 = canvasW - paddingRight,
                    Y1 = yPos,
                    Y2 = yPos,
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)(i == 0 ? 70 : 36), 148, 163, 184)),
                    StrokeThickness = i == 0 ? 1.2 : 0.7,
                    StrokeDashArray = i == 0 ? null : new DoubleCollection { 3, 4 }
                });

                var label = new TextBlock
                {
                    Text = FormatAxis(yVal),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    Width = paddingLeft - 6,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, yPos - 8);
                ChartCanvas.Children.Add(label);
            }
        }

        private static string CompactLabel(string? label, double width)
        {
            var text = label ?? "";
            if (width >= 48) return text;
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : text;
        }

        private static string FormatAxis(double value)
        {
            if (value >= 1000) return $"{value / 1000:N0}K";
            return $"{value:N0}";
        }
    }
}
