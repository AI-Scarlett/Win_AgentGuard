using System.Windows;
using AgentGuard.App.ViewModels;

namespace AgentGuard.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
