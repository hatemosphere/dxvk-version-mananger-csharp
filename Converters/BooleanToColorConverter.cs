using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DxvkVersionManager.Converters;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

public class BooleanToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            string trueResourceName = "SuccessBrush";
            string falseResourceName = "TextSecondaryBrush";
            
            // Check if parameter is provided in format "TrueValue|FalseValue"
            if (parameter is string paramStr && paramStr.Contains('|'))
            {
                var parts = paramStr.Split('|');
                if (parts.Length == 2)
                {
                    trueResourceName = parts[0] + "Brush";
                    falseResourceName = parts[1] + "Brush";
                }
            }
            
            // Look up the resource in App.xaml resources
            var resourceKey = boolValue ? trueResourceName : falseResourceName;
            if (Application.Current.Resources.Contains(resourceKey))
            {
                return Application.Current.Resources[resourceKey];
            }
        }
        
        // Default return gray if resource not found or value not boolean
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // This converter doesn't support two-way binding
        throw new NotImplementedException();
    }
} 