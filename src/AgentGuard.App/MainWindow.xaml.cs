using System.Windows;
using AgentGuard.App.Diagnostics;
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
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("Main window startup failed.", ex);
            _viewModel.ReportStartupError(ex);
            MessageBox.Show(
                $"AgentGuard could not finish startup.\n\nLog: {AppDiagnostics.LogPath}\n\n{ex.Message}",
                "AgentGuard",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        try
        {
            _viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("Main window shutdown failed.", ex);
        }
    }
}
