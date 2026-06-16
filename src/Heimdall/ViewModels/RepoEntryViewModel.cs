using CommunityToolkit.Mvvm.ComponentModel;
using Heimdall.Core.Models;

namespace Heimdall.ViewModels;

/// <summary>
/// Editable view of one watched repo. <see cref="Owner"/>/<see cref="Name"/>/<see cref="DefaultBranch"/>
/// are fixed once added; the announce settings are user-editable. Maps to and from <see cref="RepoConfig"/>.
/// </summary>
public sealed partial class RepoEntryViewModel : ObservableObject
{
    public RepoEntryViewModel(RepoConfig repo)
    {
        Owner = repo.Owner;
        Name = repo.Name;
        DefaultBranch = repo.DefaultBranch;
        AnnounceWorkflowsText = string.Join(", ", repo.AnnounceWorkflows);
        AnnounceFailures = repo.AnnounceFailures;
    }

    public string Owner { get; }

    public string Name { get; }

    public string DefaultBranch { get; }

    [ObservableProperty]
    private string _announceWorkflowsText = string.Empty;

    [ObservableProperty]
    private bool _announceFailures;

    /// <summary>Projects the edits back into a <see cref="RepoConfig"/> (split, trim, drop empties).</summary>
    public RepoConfig ToRepoConfig() => new(Owner, Name, DefaultBranch)
    {
        AnnounceWorkflows = AnnounceWorkflowsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        AnnounceFailures = AnnounceFailures
    };
}
