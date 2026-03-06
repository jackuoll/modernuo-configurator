using CommunityToolkit.Mvvm.ComponentModel;

namespace ModernUOConfigurator.ViewModels;

public partial class SettingItem : ObservableObject
{
    public string Key { get; init; } = "";
    public string DisplayName { get; init; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBool), nameof(IsNotBool), nameof(BoolValue))]
    private string _value = "";

    public bool IsBool =>
        Value.Equals("True", StringComparison.OrdinalIgnoreCase) ||
        Value.Equals("False", StringComparison.OrdinalIgnoreCase);

    public bool IsNotBool => !IsBool;

    public bool BoolValue
    {
        get => Value.Equals("True", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "True" : "False";
    }
}
