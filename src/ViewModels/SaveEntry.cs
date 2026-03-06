using CommunityToolkit.Mvvm.ComponentModel;

namespace ModernUOConfigurator.ViewModels;

public partial class SaveEntry : ObservableObject
{
    [ObservableProperty] private string _folderName = "";
    [ObservableProperty] private string _fullPath = "";
    [ObservableProperty] private string _description = "";
}
