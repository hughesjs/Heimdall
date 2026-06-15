using Heimdall.Core.Models;

namespace Heimdall;

/// <summary>Maps the aggregate tray status to its icon asset URI. Pure, so it is unit-tested directly.</summary>
internal static class TrayIconAssets
{
    public static string ResourceFor(TrayStatus status) => status switch
    {
        TrayStatus.Green => "avares://Heimdall/Assets/tray-green.png",
        TrayStatus.Red => "avares://Heimdall/Assets/tray-red.png",
        TrayStatus.Amber => "avares://Heimdall/Assets/tray-amber.png",
        _ => "avares://Heimdall/Assets/tray-grey.png"
    };
}
