using Heimdall.Core.Models;

namespace Heimdall.Core.Notifications;

/// <summary>Formats a notification's title and body from a transition. Pure, so it is unit-tested directly.</summary>
public static class NotificationContent
{
    public static (string Title, string Body) Format(NotificationPayload payload)
    {
        var run = payload.Run;
        var title = payload.Kind == TransitionKind.Broke
            ? $"{run.RepoName}: {run.WorkflowName} broke"
            : $"{run.RepoName}: {run.WorkflowName} recovered";

        var target = run.PullRequestNumbers.Count > 0 ? $"PR #{run.PullRequestNumbers[0]}" : run.HeadBranch;
        var body = $"{run.RepoOwner}/{run.RepoName} · {target} · {run.TriggeringActorLogin}";

        return (title, body);
    }
}
