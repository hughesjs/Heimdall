using Heimdall.Core.Models;
using Shouldly;

namespace Heimdall.UiTests;

public class TrayIconAssetsTests
{
    [Theory]
    [InlineData(TrayStatus.Green, "tray-green.png")]
    [InlineData(TrayStatus.Red, "tray-red.png")]
    [InlineData(TrayStatus.Amber, "tray-amber.png")]
    [InlineData(TrayStatus.Grey, "tray-grey.png")]
    public void Maps_each_status_to_its_icon(TrayStatus status, string expectedFile)
    {
        TrayIconAssets.ResourceFor(status).ShouldEndWith(expectedFile);
    }
}
