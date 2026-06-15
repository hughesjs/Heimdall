namespace Heimdall.Core.Models;

/// <summary>
/// A repository the developer watches, together with its default branch (resolved and cached when
/// the repo is added). Passed to relevance rules so the default-branch rule needs no extra lookup.
/// </summary>
public record RepoConfig(string Owner, string Name, string DefaultBranch);
