using Shouldly;

namespace Heimdall.Tests;

public class CiRedDemoTests
{
    // Intentionally failing: pushed to make the CI pipeline go red so Heimdall can be seen reacting
    // (tray → red, "broke" notification). Delete this file / revert this commit to return CI to green.
    [Fact]
    public void Deliberately_failing_to_demonstrate_a_red_pipeline()
    {
        true.ShouldBeFalse("intentional failure to demonstrate Heimdall turning the tray red");
    }
}
