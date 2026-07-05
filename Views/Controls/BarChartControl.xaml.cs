using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Brushes = System.Windows.Media.Brushes;
using GroceryPOS.Models;

namespace GroceryPOS.Views.Controls
{
    /// <summary>
    /// Pure WPF Canvas bar chart — no third-party libraries.
    /// Binds to IEnumerable&lt;ChartDataPoint&gt; via the DataSource dependency property.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public partial class BarChartControl : UserControl
    {
        // ── Dependency Properties ──────────────────────────────────────────────

        public static readonly DependencyProperty DataSourceProperty =
            DependencyProperty.Register(nameof(DataSource), typeof(IEnumerable), typeof(BarChartControl),
                new PropertyMetadata(null, OnDataSourceChanged));

        public static readonly DependencyProperty ChartTitleProperty =
            DependencyProperty.Register(nameof(ChartTitle), typeof(string), typeof(BarChartControl),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty BarColorProperty =
            DependencyProperty.Register(nameof(BarColor), typeof(Color), typeof(BarChartControl),
                new PropertyMetadata(Color.FromRgb(20, 184, 166))); // teal

        public static readonly DependencyProperty ShowSecondaryBarProperty =
            DependencyProperty.Register(nameof(ShowSecondaryBar), typeof(bool), typeof(BarChartControl),
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

        // ── Constructor ────────────────────────────────────────────────────────

        public BarChartControl()
        {
            InitializeComponent();
            SizeChanged += (_, _) => Render();
        }

        // ── Change Handlers ────────────────────────────────────────────────────

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

        // ── Rendering ─────────────────────────────────────────────────────────

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

            double canvasW = Math.Max(ChartCanvas.ActualWidth, 200);
            double canvasH = Math.Max(ChartCanvas.ActualHeight, 120);

            const double paddingLeft   = 52;
            const double paddingRight  = 12;
            const double paddingTop    = 24;
            const double paddingBottom = 36; // room for x labels

            double plotW = canvasW - paddingLeft - paddingRight;
            double plotH = canvasH - paddingTop - paddingBottom;

            double maxVal = points.Max(p => Math.Max(p.Value, p.SecondaryValue));
            if (maxVal <= 0) maxVal = 1;

            // ── Y-Axis grid lines ──────────────────────────────────────────────
            int gridLines = 4;
            for (int i = 0; i <= gridLines; i++)
            {
                double yFrac = (double)i / gridLines;
                double yPos  = paddingTop + plotH - yFrac * plotH;
                double yVal  = yFrac * maxVal;

                var line = new Line
                {
                    X1 = paddingLeft, X2 = canvasW - paddingRight,
                    Y1 = yPos, Y2 = yPos,
                    Stroke = new SolidColorBrush(Color.FromArgb(50, 100, 116, 139)),
                    StrokeThickness = i == 0 ? 1.5 : 0.8,
                    StrokeDashArray = i == 0 ? null : new DoubleCollection { 4, 4 }
                };
                ChartCanvas.Children.Add(line);

                // Y label
                var label = new TextBlock
                {
                    Text = yVal >= 1000 ? $"{yVal / 1000:N0}K" : $"{yVal:N0}",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                };
                Canvas.SetLeft(label, 2);
                Canvas.SetTop(label, yPos - 8);
                ChartCanvas.Children.Add(label);
            }

            // ── Bars ──────────────────────────────────────────────────────────
            double barGroupW = plotW / points.Count;
            bool showSecondary = ShowSecondaryBar && points.Any(p => p.SecondaryValue > 0);
            double barW    = showSecondary ? barGroupW * 0.38 : barGroupW * 0.55;
            double barGap  = showSecondary ? barGroupW * 0.06 : 0;

            var primaryBrush = new LinearGradientBrush(
                BarColor, Color.FromArgb(180, BarColor.R, BarColor.G, BarColor.B),
                new Point(0, 0), new Point(0, 1));

            var secondaryBrush = new LinearGradientBrush(
                Color.FromRgb(251, 113, 133), Color.FromArgb(160, 251, 113, 133),
                new Point(0, 0), new Point(0, 1));

            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                double groupX = paddingLeft + i * barGroupW;

                // Primary bar
                double barH = Math.Max(2, (pt.Value / maxVal) * plotH);
                double barX = groupX + (barGroupW - (showSecondary ? barW * 2 + barGap : barW)) / 2;
                double barY = paddingTop + plotH - barH;

                var bar = new Rectangle
                {
                    Width  = barW,
                    Height = barH,
                    Fill   = pt.BarColor.HasValue
                        ? new SolidColorBrush(pt.BarColor.Value)
                        : primaryBrush,
                    RadiusX = 3, RadiusY = 3
                };
                Canvas.SetLeft(bar, barX);
                Canvas.SetTop(bar, barY);
                ChartCanvas.Children.Add(bar);

                // Primary value label
                if (barH > 18)
                {
                    var valLabel = new TextBlock
                    {
                        Text = pt.DisplayValue,
                        FontSize = 8.5,
                        Foreground = Brushes.White,
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = new RotateTransform(-90)
                    };
                    Canvas.SetLeft(valLabel, barX + barW / 2 - 5);
                    Canvas.SetTop(valLabel, barY + 4);
                    ChartCanvas.Children.Add(valLabel);
                }

                // Secondary (returns) bar
                if (showSecondary && pt.SecondaryValue > 0)
                {
                    double secH = Math.Max(2, (pt.SecondaryValue / maxVal) * plotH);
                    double secX = barX + barW + barGap;
                    double secY = paddingTop + plotH - secH;

                    var secBar = new Rectangle
                    {
                        Width  = barW,
                        Height = secH,
                        Fill   = secondaryBrush,
                        RadiusX = 3, RadiusY = 3
                    };
                    Canvas.SetLeft(secBar, secX);
                    Canvas.SetTop(secBar, secY);
                    ChartCanvas.Children.Add(secBar);
                }

                // X-axis label
                var xLabel = new TextBlock
                {
                    Text = pt.Label,
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    TextAlignment = TextAlignment.Center,
                    Width = barGroupW,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Canvas.SetLeft(xLabel, groupX);
                Canvas.SetTop(xLabel, paddingTop + plotH + 6);
                ChartCanvas.Children.Add(xLabel);
            }
        }
    }
}
