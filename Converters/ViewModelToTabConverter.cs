using DxvkVersionManager.ViewModels;
using System.Globalization;
using System.Windows.Data;

namespace DxvkVersionManager.Converters;

public class ViewModelToTabConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return "Unselected";
            
        var vmType = value.GetType();
        var paramString = parameter.ToString();
        
        if (paramString == "InstalledGames" && vmType == typeof(InstalledGamesViewModel))
            return "Selected";
        
        if (paramString == "DxvkVersions" && vmType == typeof(DxvkVersionsViewModel))
            return "Selected";
        
        if (paramString == "DxvkGplasync" && vmType == typeof(DxvkGplasyncViewModel))
            return "Selected";
            
        return "Unselected";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}