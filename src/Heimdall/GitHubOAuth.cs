namespace Heimdall;

/// <summary>
/// The public OAuth App identity used for the device flow. The client id is NOT a secret and ships in
/// the binary. Register an OAuth App with Device Flow enabled and "expiring user tokens" OFF, then
/// replace <see cref="ClientId"/>. Read-only <c>repo</c> scope is sufficient for private repos.
/// </summary>
internal static class GitHubOAuth
{
    // TODO: replace with the registered OAuth App client id before shipping / live use.
    public const string ClientId = "REPLACE_WITH_OAUTH_APP_CLIENT_ID";

    public const string Scope = "repo";
}
