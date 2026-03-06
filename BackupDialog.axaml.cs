using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ModernUOConfigurator;

public partial class BackupDialog : Window
{
    public BackupDialog() => InitializeComponent();

    private void OnBackup(object? sender, RoutedEventArgs e) => TryConfirm();

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
        Close(new BackupDialogResult(name, DescriptionBox.Text?.Trim() ?? ""));
    }
}

public record BackupDialogResult(string Name, string Description);
