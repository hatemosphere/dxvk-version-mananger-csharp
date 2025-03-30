using System;
using System.Globalization;
using System.Windows.Data;

namespace DxvkVersionManager
{
    public class BooleanToStringConverter : IValueConverter
    {
        public string TrueValue { get; set; } = "True";
        public string FalseValue { get; set; } = "False";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // Check if parameter format is "TrueValue|FalseValue"
                if (parameter is string paramString && paramString.Contains("|"))
                {
                    var parts = paramString.Split('|');
                    if (parts.Length == 2)
                    {
                        return boolValue ? parts[0] : parts[1];
                    }
                }
                
                return boolValue ? TrueValue : FalseValue;
            }
            
            return FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue)
            {
                if (parameter is string paramString && paramString.Contains("|"))
                {
                    var parts = paramString.Split('|');
                    if (parts.Length == 2)
                    {
                        return stringValue == parts[0];
                    }
                }
                
                return stringValue == TrueValue;
            }
            
            return false;
        }
    }
} 