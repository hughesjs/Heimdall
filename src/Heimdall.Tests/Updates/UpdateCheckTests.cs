using System;
using Heimdall.Core.Updates;
using Shouldly;

namespace Heimdall.Tests.Updates;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("1.4.0", "v1.4.1", true)]        // newer patch
    [InlineData("1.4.0", "v1.5.0", true)]        // newer minor
    [InlineData("1.4.0", "v2.0.0", true)]        // newer major
    [InlineData("1.4.0", "1.5.0", true)]         // missing 'v' prefix still parses
    [InlineData("1.4.0", "v1.4.0", false)]       // equal
    [InlineData("1.4.0", "v1.3.9", false)]       // older
    [InlineData("1.4.0", "v1.5.0-rc.1", true)]   // prerelease suffix dropped
    [InlineData("1.4.0", "v1.5.0+abcdef", true)] // build metadata dropped
    [InlineData("1.4.0", "v1.4.0+abcdef", false)]// equal once metadata dropped
    [InlineData("1.4.0", "v2", true)]            // single component
    [InlineData("1.9.0", "v2.1", true)]          // two components
    [InlineData("1.4.0", "garbage", false)]      // unparseable -> no update
    [InlineData("1.4.0", "", false)]             // empty -> no update
    [InlineData("1.4.0", "v", false)]            // bare prefix -> no update
    public void Reports_update_availability(string current, string latestTag, bool expected)
    {
        UpdateCheck.IsUpdateAvailable(Version.Parse(current), latestTag).ShouldBe(expected);
    }

    [Fact]
    public void Four_part_current_equal_to_three_part_tag_is_not_an_update()
    {
        // Running assembly versions are X.Y.Z.0; the tag is vX.Y.Z. These must compare equal.
        UpdateCheck.IsUpdateAvailable(new Version(1, 4, 0, 0), "v1.4.0").ShouldBeFalse();
    }
}
