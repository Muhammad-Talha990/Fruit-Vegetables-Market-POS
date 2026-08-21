using System.Windows.Media;

namespace FruitVegetableMarketPOS.Models
{
    /// <summary>
    /// A single data point for chart rendering (bar chart, line chart, etc.)
    /// </summary>
    public class ChartDataPoint
    {
        /// <summary>X-axis label (e.g. "Mon", "Jul 1", "Item Name")</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Primary value (e.g. sales revenue)</summary>
        public double Value { get; set; }

        /// <summary>Secondary value (e.g. returns amount), used for grouped bars</summary>
        public double SecondaryValue { get; set; }

        /// <summary>Optional bar fill color. Null means use theme default.</summary>
        public System.Windows.Media.Color? BarColor { get; set; }

        /// <summary>When false, bar labels show plain quantity instead of rupees.</summary>
        public bool FormatAsCurrency { get; set; } = true;

        /// <summary>Formatted display value (e.g. "Rs. 2,500")</summary>
        public string DisplayValue
        {
            get
            {
                if (!FormatAsCurrency)
                    return Value.ToString("0.###");
                return $"Rs.{Value:N0}";
            }
        }
    }
}
