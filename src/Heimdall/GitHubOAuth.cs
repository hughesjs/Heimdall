namespace Heimdall;

/// <summary>
/// The public OAuth App identity used for the device flow. The client id is NOT a secret and ships in
/// the binary. The OAuth App has Device Flow enabled; its user tokens are non-expiring by default
/// (OAuth Apps have no expiry toggle), which matches the no-refresh design. The <c>repo</c> scope is
/// sufficient for private repos.
/// </summary>
internal static class GitHubOAuth
{
    public const string ClientId = "Ov23li37aNxDVYrY7XQi";

    public const string Scope = "repo";
}
