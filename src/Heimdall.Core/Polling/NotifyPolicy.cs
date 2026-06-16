namespace Heimdall.Core.Polling;

/// <summary>How a pipeline line decides when to notify when its latest run is folded in.</summary>
public enum NotifyPolicy
{
    /// <summary>Notify only on settled green↔red transitions (the default rule-based behaviour).</summary>
    Transitions,

    /// <summary>Notify on each new settled run of an announce workflow (release announcements).</summary>
    Announce
}
