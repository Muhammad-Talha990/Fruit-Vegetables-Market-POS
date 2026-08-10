using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FruitVegetableMarketPOS.Converters
{
    /// <summary>
    /// Maps a fruit/veg icon key to a colorful DrawingImage from FruitIcons.xaml.
    /// </summary>
    public class FruitIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = value as string;
            if (string.IsNullOrWhiteSpace(key))
                key = "default";

            var resourceKey = $"FruitIcon_{key}";
            if (Application.Current?.TryFindResource(resourceKey) is ImageSource src)
                return src;

            return Application.Current?.TryFindResource("FruitIcon_default") as ImageSource
                   ?? DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
