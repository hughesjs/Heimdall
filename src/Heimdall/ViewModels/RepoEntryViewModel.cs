using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Heimdall.Core.Models;

namespace Heimdall.ViewModels;

/// <summary>
/// Editable view of one watched repo. <see cref="Owner"/>/<see cref="Name"/>/<see cref="DefaultBranch"/>
/// are fixed once added; the announce settings are user-editable. Maps to and from <see cref="RepoConfig"/>.
/// </summary>
public sealed partial class RepoEntryViewModel : ObservableObject
{
    public RepoEntryViewModel(RepoConfig repo, IReadOnlyList<string> availableWorkflows)
    {
        Owner = repo.Owner;
        Name = repo.Name;
        DefaultBranch = repo.DefaultBranch;
        AnnounceFailures = repo.AnnounceFailures;

        // Show every available workflow, plus any configured announce name not in the fetched list (e.g. the
        // listing failed or a workflow was renamed) so existing config is never silently dropped.
        var names = availableWorkflows
            .Concat(repo.AnnounceWorkflows)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
            Workflows.Add(new WorkflowToggle(name, repo.AnnounceWorkflows.Contains(name, StringComparer.OrdinalIgnoreCase)));
    }

    public string Owner { get; }

    public string Name { get; }

    public string DefaultBranch { get; }

    /// <summary>The repo's workflows, each toggleable as an announce workflow.</summary>
    public ObservableCollection<WorkflowToggle> Workflows { get; } = [];

    [ObservableProperty]
    private bool _announceFailures;

    /// <summary>Projects the edits back into a <see cref="RepoConfig"/> (the ticked workflows announce).</summary>
    public RepoConfig ToRepoConfig() => new(Owner, Name, DefaultBranch)
    {
        AnnounceWorkflows = [.. Workflows.Where(workflow => workflow.IsAnnounce).Select(workflow => workflow.Name)],
        AnnounceFailures = AnnounceFailures
    };
}
