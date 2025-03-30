using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DxvkVersionManager.Converters;

public class ObjectNotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNotNull = value != null;
        
        // Check if we need to invert the result
        if (parameter != null && parameter.ToString() == "Inverse")
        {
            isNotNull = !isNotNull;
        }
        
        return isNotNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("Converting back from Visibility to object is not supported.");
    }
} 