using System.Globalization;
using System.Windows.Data;

namespace FxTradeConfirmation.Converters;

public class BoolToConnectionTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Connected" : "Disconnected";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}