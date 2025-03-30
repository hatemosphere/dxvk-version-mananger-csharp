using DxvkVersionManager.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DxvkVersionManager.Views;

public partial class DxvkSelectionDialog : UserControl
{
    public DxvkSelectionDialog()
    {
        InitializeComponent();
        
        // Subscribe to the CloseRequested event when the DataContext changes
        DataContextChanged += DxvkSelectionDialog_DataContextChanged;
    }
    
    private void DxvkSelectionDialog_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        // If previous DataContext was a DxvkSelectionDialogViewModel, unsubscribe
        if (e.OldValue is DxvkSelectionDialogViewModel oldViewModel)
        {
            oldViewModel.CloseRequested -= ViewModel_CloseRequested;
        }
        
        // If new DataContext is a DxvkSelectionDialogViewModel, subscribe
        if (e.NewValue is DxvkSelectionDialogViewModel newViewModel)
        {
            newViewModel.CloseRequested += ViewModel_CloseRequested;
        }
    }
    
    private void ViewModel_CloseRequested()
    {
        // No need to do anything - parent will handle this through the event
    }
    
    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // Directly call the command on the ViewModel
        if (DataContext is DxvkSelectionDialogViewModel viewModel)
        {
            viewModel.CloseDialogCommand.Execute(null);
        }
    }
}