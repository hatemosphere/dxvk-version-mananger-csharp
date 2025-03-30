# DXVK Version Manager (C# Edition)

A Windows utility to manage versions of DXVK in installed games. This application allows you to apply DXVK (DirectX to Vulkan translation layer) to Windows games to improve performance or compatibility.

## Features

- Scan for installed Steam games
- Download and manage DXVK releases from the official repository
- Download and manage DXVK-gplasync releases (a variant with asynchronous pipeline compilation)
- Apply DXVK to games with automatic backup of original DLLs
- Restore original DLLs from backups
- Track which games have DXVK applied and which version

## Requirements

- Windows 10/11
- .NET 8.0 or later
- Steam (for game detection)

## Building the Project

1. Clone the repository
2. Open the solution in Visual Studio 2022 or later
3. Build the solution

```
dotnet build
```

## Development

This project uses:
- .NET 8.0
- WPF for the user interface
- MVVM pattern with CommunityToolkit.Mvvm
- Dependency Injection for service management

## Project Structure

- **Models**: Data models for games, DXVK versions, etc.
- **ViewModels**: MVVM ViewModels 
- **Views**: WPF XAML Views
- **Services**: Business logic for Steam, DXVK, etc.
- **Utils**: Helper utilities
- **Converters**: Value converters for XAML binding

## Background

This project is a C# rewrite of the original [DXVK Version Manager](https://github.com/artmakh/dxvk-version-mananger) which was built with Electron/JavaScript.

## License

MIT License