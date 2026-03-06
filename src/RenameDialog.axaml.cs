using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ModernUOConfigurator;

public partial class RenameDialog : Window
{
    public RenameDialog() => InitializeComponent();

    public RenameDialog(string currentName) : this()
    {
        NameBox.Text = currentName;
        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OnRename(object? sender, RoutedEventArgs e) => TryConfirm();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            TryConfirm();
    }

    private void TryConfirm()
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        Close(name);
    }
}
