using Avalonia.Controls;
using Avalonia.Interactivity;
using Heimdall.Platform;
using Heimdall.ViewModels;

namespace Heimdall.Views;

public partial class DeviceFlowWindow : Window
{
    public DeviceFlowWindow() => InitializeComponent();

    public void OpenPage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DeviceFlowViewModel viewModel && !string.IsNullOrEmpty(viewModel.VerificationUri))
            Shell.OpenUrl(viewModel.VerificationUri);
    }
}
