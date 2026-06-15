namespace Heimdall.Core.Models;

/// <summary>
/// Who "I" am for relevance matching. MVP is the GitHub login; commit author emails are added
/// alongside the commit-range rule in the fast-follow.
/// </summary>
public record Identity(string Login);
