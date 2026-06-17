using Heimdall.Platform;
using Shouldly;

namespace Heimdall.UiTests;

public class ShellTests
{
    [Fact]
    public void BuildStartInfo_passes_arguments_as_distinct_argv_entries_without_a_shell()
    {
        // The args must reach the child as separate argv entries, verbatim — never concatenated into a
        // command line a shell would re-parse. A value beginning with `-` or containing quotes/spaces must
        // survive untouched (this is what lets the notification managers pass untrusted text safely).
        var info = Shell.BuildStartInfo("osascript",
            ["-e", "on run argv", "--", "-wip", "build \"failed\" with 'quotes'"], environment: null);

        info.ArgumentList.ShouldBe(["-e", "on run argv", "--", "-wip", "build \"failed\" with 'quotes'"]);
        info.UseShellExecute.ShouldBeFalse();
    }

    [Fact]
    public void BuildStartInfo_copies_environment_variables_onto_the_child_verbatim()
    {
        // The Windows notification path hands title/body to PowerShell via the environment. Special
        // characters must land in the child's environment exactly as given — not escaped or mangled —
        // since the whole point is that they are never parsed as script.
        var environment = new Dictionary<string, string>
        {
            ["HEIMDALL_TOAST_TITLE"] = "O'Brien & co.",
            ["HEIMDALL_TOAST_BODY"] = "build \"failed\" \\ 100% $(whoami)"
        };

        var info = Shell.BuildStartInfo("powershell", ["-NoProfile", "-Command", "$env:HEIMDALL_TOAST_TITLE"], environment);

        info.Environment["HEIMDALL_TOAST_TITLE"].ShouldBe("O'Brien & co.");
        info.Environment["HEIMDALL_TOAST_BODY"].ShouldBe("build \"failed\" \\ 100% $(whoami)");
    }
}
