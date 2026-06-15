namespace Heimdall.Core.Models;

/// <summary>
/// Aggregate tray indicator across a developer's relevant pipelines.
/// Priority when aggregating: Grey ≻ Red ≻ Amber ≻ Green.
/// </summary>
public enum TrayStatus
{
    /// <summary>All relevant pipelines healthy.</summary>
    Green,

    /// <summary>At least one relevant pipeline is failing.</summary>
    Red,

    /// <summary>Relevant run(s) in progress and nothing failing.</summary>
    Amber,

    /// <summary>Not connected or in an error state.</summary>
    Grey
}
