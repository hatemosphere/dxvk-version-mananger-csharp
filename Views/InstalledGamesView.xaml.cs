using DxvkVersionManager.ViewModels;
using DxvkVersionManager.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using DxvkVersionManager.Services.Implementations;

namespace DxvkVersionManager.Views;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class InstalledGamesView : UserControl
{
    // Add a logger field
    private readonly LoggingService _logger = LoggingService.Instance;
    
    public InstalledGamesView()
    {
        InitializeComponent();
    }
    
    private void DirectXComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!(sender is ComboBox comboBox) || comboBox.SelectedItem == null || comboBox.DataContext == null)
            return;
        
        try
        {
            // Pass the tag (AppId) along with the selection
            string? appId = comboBox.Tag?.ToString();
            
            // Only trigger if it's a real selection (not initialized)
            if (appId != null && comboBox.SelectedIndex > 0 && this.DataContext is InstalledGamesViewModel viewModel)
            {
                string option = ((ComboBoxItem)comboBox.SelectedItem).Content?.ToString() ?? string.Empty;
                viewModel.UpdateDirectXVersionCommand.Execute((appId, option));
            }
        }
        catch (Exception ex)
        {
            // Log error - in a production app you'd use a proper logger
            _logger.LogError(ex, "Error in DirectXComboBox_SelectionChanged");
        }
    }
    
    private void ArchitectureComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!(sender is ComboBox comboBox) || comboBox.SelectedItem == null || comboBox.DataContext == null)
            return;
            
        try
        {
            // Pass the tag (AppId) along with the selection
            string? appId = comboBox.Tag?.ToString();
            
            // Only trigger if it's a real selection (not initialized)
            if (appId != null && comboBox.SelectedIndex >= 0 && this.DataContext is InstalledGamesViewModel viewModel)
            {
                string option = ((ComboBoxItem)comboBox.SelectedItem).Content?.ToString() ?? string.Empty;
                viewModel.UpdateArchitectureCommand.Execute((appId, option));
            }
        }
        catch (Exception ex)
        {
            // Log error - in a production app you'd use a proper logger
            _logger.LogError(ex, "Error in ArchitectureComboBox_SelectionChanged");
        }
    }
    
    private void ArchitectureComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (!(sender is ComboBox comboBox) || comboBox.DataContext == null)
            return;
            
        try
        {
            // Get the game data from DataContext
            var game = comboBox.DataContext as SteamGame;
            if (game?.Metadata == null)
                return;
                
            // Set the combo box selection based on the game's architecture settings
            if (game.Metadata.Executable32bit && !game.Metadata.Executable64bit)
            {
                comboBox.SelectedIndex = 0; // 32-bit (index is now 0 since we removed the placeholder)
            }
            else if (!game.Metadata.Executable32bit && game.Metadata.Executable64bit)
            {
                comboBox.SelectedIndex = 1; // 64-bit (index is now 1 since we removed the placeholder)
            }
            else
            {
                comboBox.SelectedIndex = -1; // No selection
            }
        }
        catch (Exception ex)
        {
            // Log error
            _logger.LogError(ex, "Error in ArchitectureComboBox_Loaded");
        }
    }
    
    private void DirectXComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (!(sender is ComboBox comboBox) || comboBox.DataContext == null)
            return;
            
        try
        {
            // Get the game data from DataContext
            var game = comboBox.DataContext as SteamGame;
            if (game?.Metadata == null)
                return;
            
            // Set the combo box selection based on the game's DirectX version
            var d3dVersion = game.Metadata.Direct3dVersions;
            
            if (string.IsNullOrEmpty(d3dVersion) || d3dVersion == "Unknown")
            {
                comboBox.SelectedIndex = 0; // Select "Choose Direct3D version"
                return;
            }
            
            // Find the matching DirectX version in the combo box
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Content?.ToString()?.Contains(d3dVersion) == true)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
            
            // If no match found, select "Choose Direct3D version"
            comboBox.SelectedIndex = -1; // No selection
        }
        catch (Exception ex)
        {
            // Log error
            _logger.LogError(ex, "Error in DirectXComboBox_Loaded");
        }
    }
}