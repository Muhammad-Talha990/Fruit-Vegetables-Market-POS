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
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Views.Controls
{
    /// <summary>
    /// Pure WPF Canvas bar chart — no third-party libraries.
    /// Value labels sit above bars (not rotated inside) so amounts stay fully visible.
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
                new PropertyMetadata(Color.FromRgb(20, 184, 166)));

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

        public BarChartControl()
        {
            InitializeComponent();
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

            // Extra top room for bold value labels above bars; bottom for wrapped x labels
            const double paddingLeft = 56;
            const double paddingRight = 10;
            const double paddingTop = 28;
            const double paddingBottom = 52;

            double plotW = canvasW - paddingLeft - paddingRight;
            double plotH = canvasH - paddingTop - paddingBottom;

            double maxVal = points.Max(p => Math.Max(p.Value, p.SecondaryValue));
            if (maxVal <= 0) maxVal = 1;
            // Leave headroom so tallest bar never covers its own label
            maxVal *= 1.18;

            int gridLines = 4;
            for (int i = 0; i <= gridLines; i++)
            {
                double yFrac = (double)i / gridLines;
                double yPos = paddingTop + plotH - yFrac * plotH;
                double yVal = yFrac * (maxVal / 1.18);

                var line = new Line
                {
                    X1 = paddingLeft,
                    X2 = canvasW - paddingRight,
                    Y1 = yPos,
                    Y2 = yPos,
                    Stroke = new SolidColorBrush(Color.FromArgb(50, 100, 116, 139)),
                    StrokeThickness = i == 0 ? 1.5 : 0.8,
                    StrokeDashArray = i == 0 ? null : new DoubleCollection { 4, 4 }
                };
                ChartCanvas.Children.Add(line);

                var label = new TextBlock
                {
                    Text = FormatAxis(yVal),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
                };
                Canvas.SetLeft(label, 2);
                Canvas.SetTop(label, yPos - 8);
                ChartCanvas.Children.Add(label);
            }

            double barGroupW = plotW / points.Count;
            bool showSecondary = ShowSecondaryBar && points.Any(p => p.SecondaryValue > 0);
            double barW = showSecondary ? barGroupW * 0.36 : Math.Min(barGroupW * 0.62, 72);
            double barGap = showSecondary ? barGroupW * 0.06 : 0;

            var primaryBrush = new LinearGradientBrush(
                BarColor, Color.FromArgb(180, BarColor.R, BarColor.G, BarColor.B),
                new Point(0, 0), new Point(0, 1));

            var secondaryBrush = new LinearGradientBrush(
                Color.FromRgb(251, 113, 133), Color.FromArgb(160, 251, 113, 133),
                new Point(0, 0), new Point(0, 1));

            var valueBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            var xLabelBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59));

            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                double groupX = paddingLeft + i * barGroupW;

                double barH = Math.Max(4, (pt.Value / maxVal) * plotH);
                double barX = groupX + (barGroupW - (showSecondary ? barW * 2 + barGap : barW)) / 2;
                double barY = paddingTop + plotH - barH;

                var bar = new Rectangle
                {
                    Width = barW,
                    Height = barH,
                    Fill = pt.BarColor.HasValue
                        ? new SolidColorBrush(pt.BarColor.Value)
                        : primaryBrush,
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(bar, barX);
                Canvas.SetTop(bar, barY);
                ChartCanvas.Children.Add(bar);

                // Value ABOVE the bar — horizontal, bold, fully visible
                var valLabel = new TextBlock
                {
                    Text = FormatAmount(pt.Value),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = valueBrush,
                    TextAlignment = TextAlignment.Center,
                    Width = Math.Max(barGroupW - 4, 48)
                };
                valLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double valH = valLabel.DesiredSize.Height;
                Canvas.SetLeft(valLabel, groupX + 2);
                Canvas.SetTop(valLabel, Math.Max(2, barY - valH - 2));
                ChartCanvas.Children.Add(valLabel);

                if (showSecondary && pt.SecondaryValue > 0)
                {
                    double secH = Math.Max(4, (pt.SecondaryValue / maxVal) * plotH);
                    double secX = barX + barW + barGap;
                    double secY = paddingTop + plotH - secH;

                    var secBar = new Rectangle
                    {
                        Width = barW,
                        Height = secH,
                        Fill = secondaryBrush,
                        RadiusX = 4,
                        RadiusY = 4
                    };
                    Canvas.SetLeft(secBar, secX);
                    Canvas.SetTop(secBar, secY);
                    ChartCanvas.Children.Add(secBar);

                    var secLabel = new TextBlock
                    {
                        Text = FormatAmount(pt.SecondaryValue),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(190, 24, 93)),
                        TextAlignment = TextAlignment.Center,
                        Width = barW + 8
                    };
                    secLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(secLabel, secX - 4);
                    Canvas.SetTop(secLabel, Math.Max(2, secY - secLabel.DesiredSize.Height - 2));
                    ChartCanvas.Children.Add(secLabel);
                }

                // Full x-axis label — wrap instead of clipping with ellipsis
                var xLabel = new TextBlock
                {
                    Text = pt.Label ?? string.Empty,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = xLabelBrush,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Width = Math.Max(barGroupW - 2, 40),
                    LineHeight = 13,
                    MaxHeight = 44
                };
                Canvas.SetLeft(xLabel, groupX + 1);
                Canvas.SetTop(xLabel, paddingTop + plotH + 6);
                ChartCanvas.Children.Add(xLabel);
            }
        }

        private static string FormatAmount(double value)
        {
            if (value >= 100_000)
                return $"Rs.{value / 1000:N0}K";
            return $"Rs.{value:N0}";
        }

        private static string FormatAxis(double value)
        {
            if (value >= 1000)
                return $"{value / 1000:N0}K";
            return $"{value:N0}";
        }
    }
}
