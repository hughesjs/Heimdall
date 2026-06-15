using CommunityToolkit.Mvvm.ComponentModel;

namespace Heimdall.ViewModels;

/// <summary>A single relevance-rule toggle in the settings view.</summary>
public sealed partial class RuleToggle(string ruleId, string displayName, bool enabled) : ViewModelBase
{
    public string RuleId { get; } = ruleId;
    public string DisplayName { get; } = displayName;

    [ObservableProperty]
    private bool _enabled = enabled;
}
