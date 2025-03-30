using CommunityToolkit.Mvvm.ComponentModel;

namespace DxvkVersionManager.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private bool _isLoading;
    public bool IsLoading 
    { 
        get => _isLoading; 
        set => SetProperty(ref _isLoading, value); 
    }
}