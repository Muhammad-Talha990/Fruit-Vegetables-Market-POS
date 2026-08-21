using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FruitVegetableMarketPOS.Models;
using FruitVegetableMarketPOS.ViewModels;

namespace FruitVegetableMarketPOS.Views
{
    public partial class ReportsView : UserControl
    {
        public ReportsView()
        {
            InitializeComponent();
            DataContextChanged += (_, e) =>
            {
                if (IsVisible && e.NewValue is ReportsViewModel vm)
                    vm.OnActivated();
            };
            IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is true && DataContext is ReportsViewModel vm)
                    vm.OnActivated();
            };
        }

        private void ReportSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is not ReportsViewModel vm || sender is not TextBox box)
                return;
            var text = box.Text ?? "";
            if (vm.SearchQuery != text)
                vm.SearchQuery = text;
        }

        private void ItemGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ReportsViewModel vm && sender is DataGrid grid && grid.SelectedItem is ItemSalesRow row)
                vm.OpenItemCommand.Execute(row);
        }

        private void CustomerGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ReportsViewModel vm && sender is DataGrid grid && grid.SelectedItem is CustomerSalesRow row)
                vm.OpenCustomerCommand.Execute(row);
        }

        private void ReportGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not DataGrid grid) return;
            foreach (var col in grid.Columns)
                col.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
        }

        /// <summary>
        /// Trackpads send small wheel deltas. DataGrid item-scrolling often swallows them
        /// without moving. Apply pixel scroll on the inner viewer so mouse and pad both work.
        /// Shift+wheel (or a wide table with no vertical overflow) scrolls left/right.
        /// </summary>
        private void ReportGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DataGrid grid || e.Delta == 0)
                return;

            var sv = FindDescendant<ScrollViewer>(grid);
            if (sv == null)
                return;

            var useHorizontal = Keyboard.Modifiers == ModifierKeys.Shift || sv.ScrollableHeight <= 0;
            if (useHorizontal && sv.ScrollableWidth > 0)
            {
                sv.ScrollToHorizontalOffset(Clamp(sv.HorizontalOffset - e.Delta, 0, sv.ScrollableWidth));
                e.Handled = true;
                return;
            }

            if (sv.ScrollableHeight > 0)
            {
                sv.ScrollToVerticalOffset(Clamp(sv.VerticalOffset - e.Delta, 0, sv.ScrollableHeight));
                e.Handled = true;
            }
        }

        private static double Clamp(double value, double min, double max) =>
            Math.Max(min, Math.Min(max, value));

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    return match;
                var nested = FindDescendant<T>(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }
    }
}
