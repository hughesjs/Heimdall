namespace Heimdall.Core.Models;

/// <summary>
/// Emitted once when a relevant pipeline's settled status flips. The originating <see cref="RunRecord"/>
/// carries all the content a notification needs (repo, workflow, branch/PR, actor, click-through URL).
/// </summary>
public record NotificationPayload(TransitionKind Kind, RunRecord Run);
