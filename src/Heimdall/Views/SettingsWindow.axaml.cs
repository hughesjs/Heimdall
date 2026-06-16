using Avalonia.Controls;
using Avalonia.Interactivity;
using Heimdall.ViewModels;

namespace Heimdall.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    public async void AddRepo(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.AddRepoAsync(default);
    }

    public async void Save(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.SaveAsync(default);
    }

    public async void TestNotification(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.TestNotificationAsync();
    }

    public void RemoveRepo(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RepoEntryViewModel repo } && ViewModel is { } viewModel)
            viewModel.RemoveRepo(repo);
    }
}
