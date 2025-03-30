using System.Globalization;
using System.Windows.Data;
using System.Diagnostics;

namespace DxvkVersionManager.Converters;

public class DebugConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string debugInfo = $"[DEBUG] Value: {value}, Type: {value?.GetType().Name}, Parameter: {parameter}";
        Debug.WriteLine(debugInfo);
        Console.WriteLine(debugInfo);
        
        // Pass through the original value
        return value;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
} 