using Avalonia.Controls;
using ModernUOConfigurator.ViewModels;

namespace ModernUOConfigurator;

public partial class MainWindow : Window
{
    private ScrollViewer? _outputScroll;

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;

        Loaded += (_, _) =>
        {
            _outputScroll = this.FindControl<ScrollViewer>("OutputScroll");
            vm.OutputAppended += () => _outputScroll?.ScrollToEnd();
            vm.Initialize(this);
        };
    }
}
