namespace Heimdall.Core.Models;

/// <summary>Direction of a settled pipeline transition worth notifying about.</summary>
public enum TransitionKind
{
    /// <summary>Green to red — a relevant pipeline started failing.</summary>
    Broke,

    /// <summary>Red to green — a relevant pipeline recovered.</summary>
    Recovered
}
