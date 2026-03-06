using System.Collections.ObjectModel;

namespace ModernUOConfigurator.ViewModels;

public class SettingGroup
{
    public string Name { get; init; } = "";
    public ObservableCollection<SettingItem> Items { get; } = [];
}
