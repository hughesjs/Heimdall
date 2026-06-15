using Octokit;
using Octokit.Internal;

namespace Heimdall.Core.GitHub;

/// <summary>
/// Builds an authenticated <see cref="IGitHubClient"/> whose HTTP pipeline runs through the
/// <see cref="ConditionalGetHandler"/>, so all GETs get ETag conditional-request handling.
/// </summary>
public static class GitHubClientFactory
{
    public static IGitHubClient Create(string token, ConditionalGetHandler? conditionalGetHandler = null)
    {
        var handler = conditionalGetHandler ?? new ConditionalGetHandler(new HttpClientHandler());
        var httpClient = new HttpClientAdapter(() => handler);
        var connection = new Connection(new ProductHeaderValue("Heimdall"), httpClient);
        return new GitHubClient(connection) { Credentials = new Credentials(token) };
    }
}
