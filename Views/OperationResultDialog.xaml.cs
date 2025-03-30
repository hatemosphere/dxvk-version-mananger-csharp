using DxvkVersionManager.ViewModels;
using System.Windows.Controls;

namespace DxvkVersionManager.Views;

public partial class OperationResultDialog : UserControl
{
    public OperationResultDialog()
    {
        InitializeComponent();
        
        DataContextChanged += OperationResultDialog_DataContextChanged;
    }
    
    private void OperationResultDialog_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        // If previous DataContext was a OperationResultDialogViewModel, unsubscribe
        if (e.OldValue is OperationResultDialogViewModel oldViewModel)
        {
            oldViewModel.CloseRequested -= ViewModel_CloseRequested;
        }
        
        // If new DataContext is a OperationResultDialogViewModel, subscribe
        if (e.NewValue is OperationResultDialogViewModel newViewModel)
        {
            newViewModel.CloseRequested += ViewModel_CloseRequested;
        }
    }
    
    private void ViewModel_CloseRequested()
    {
        // No need to do anything - parent will handle this through the event
    }
}