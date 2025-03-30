using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DxvkVersionManager.Models;

namespace DxvkVersionManager.ViewModels;

public partial class OperationResultDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private OperationResult _result;
    
    [ObservableProperty]
    private string _headerText;
    
    public bool HasWarning => !string.IsNullOrEmpty(Result.Warning);
    
    public bool HasDetails => !string.IsNullOrEmpty(Result.Details);
    
    // Event to close the dialog
    public event Action? CloseRequested;
    
    public OperationResultDialogViewModel(OperationResult result)
    {
        _result = result;
        _headerText = result.Success ? "Operation Successful" : "Operation Failed";
        
        // For diagnostics, use a more specific header
        if (!string.IsNullOrEmpty(result.Details) && result.Success)
        {
            _headerText = "Diagnostics Completed";
        }
    }
    
    [RelayCommand]
    private void CloseDialog()
    {
        CloseRequested?.Invoke();
    }
}