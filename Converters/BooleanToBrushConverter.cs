using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DxvkVersionManager.Converters;

public class BooleanToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue 
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) // Success color
                : new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Error color
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}