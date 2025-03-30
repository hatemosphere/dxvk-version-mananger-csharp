using System.Globalization;
using System.Windows.Data;

namespace DxvkVersionManager.Converters;

public class BooleanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // If value is null, return empty string
        if (value == null)
            return string.Empty;
            
        // If value is bool and parameter is string
        if (value is bool boolValue && parameter is string stringValue)
        {
            return boolValue ? stringValue : "Inactive";
        }
        
        // Default fallback
        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}