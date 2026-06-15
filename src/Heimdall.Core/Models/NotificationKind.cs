namespace Heimdall.Core.Models;

/// <summary>What a notification is announcing: a settled green↔red transition, or a release.</summary>
public enum NotificationKind
{
    /// <summary>Green to red — a relevant pipeline started failing.</summary>
    Broke,

    /// <summary>Red to green — a relevant pipeline recovered.</summary>
    Recovered,

    /// <summary>A new settled run of an announce workflow shipped.</summary>
    Released
}
