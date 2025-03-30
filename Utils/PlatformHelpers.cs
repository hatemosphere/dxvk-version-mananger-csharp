#if WINDOWS
using System.Windows;
#endif

namespace DxvkVersionManager.Utils;

public static class PlatformHelpers
{
    public static bool IsWindows =>
#if NET
        OperatingSystem.IsWindows();
#else
        Environment.OSVersion.Platform == PlatformID.Win32NT;
#endif

    public static void ShowPlatformNotSupportedMessage()
    {
#if WINDOWS
        MessageBox.Show("This application only runs on Windows.", "Platform Not Supported", MessageBoxButton.OK, MessageBoxImage.Error);
#else
        Console.WriteLine("This application only runs on Windows.");
#endif
    }
}