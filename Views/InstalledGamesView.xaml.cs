using DxvkVersionManager.ViewModels;
using DxvkVersionManager.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using DxvkVersionManager.Services.Implementations;

namespace DxvkVersionManager.Views;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class InstalledGamesView : UserControl
{
    private readonly LoggingService _logger = LoggingService.Instance;
    
    public InstalledGamesView()
    {
        InitializeComponent();
    }
}