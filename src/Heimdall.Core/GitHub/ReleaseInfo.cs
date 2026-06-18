namespace Heimdall.Core.GitHub;

/// <summary>The app's narrow view of a GitHub release: its tag and the page a user opens to download it.</summary>
public sealed record ReleaseInfo(string TagName, string HtmlUrl);
