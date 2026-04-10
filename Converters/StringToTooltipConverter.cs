using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FxTradeConfirmation.Converters;

/// <summary>
/// Converts a non-empty string into a styled <see cref="ToolTip"/>.
/// Returns null when the string is empty so WPF suppresses the tooltip.
/// </summary>
public class StringToTooltipConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrEmpty(text))
            return null;

        var tooltip = new ToolTip
        {
            Style = Application.Current.FindResource("DarkTooltip") as Style,
            Content = new TextBlock
            {
                Text = text,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("WarningAmberBrush"),
                FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("MainFont"),
                FontSize = 11
            }
        };

        return tooltip;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}