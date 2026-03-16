using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FxTradeConfirmation.Converters;

/// <summary>
/// Converts a decimal premium value to a color:
/// Positive → Green, Negative → Red, Zero → Neutral
/// </summary>
public class PremiumColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
        {
            if (d > 0) return new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Green
            if (d < 0) return new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)); // Red
        }
        return new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)); // Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
