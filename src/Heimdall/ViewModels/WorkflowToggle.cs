using CommunityToolkit.Mvvm.ComponentModel;

namespace Heimdall.ViewModels;

/// <summary>A repo workflow with a toggle for whether its successful runs announce a release.</summary>
public sealed partial class WorkflowToggle(string name, bool isAnnounce) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isAnnounce = isAnnounce;
}
