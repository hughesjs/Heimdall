namespace Heimdall.Core.Models;

/// <summary>
/// Emitted once when a pipeline notifies — a settled green↔red transition or an announce-workflow release.
/// The originating <see cref="RunRecord"/> carries all the content a notification needs (repo, workflow,
/// branch/PR, actor, click-through URL).
/// </summary>
public record NotificationPayload(NotificationKind Kind, RunRecord Run);
