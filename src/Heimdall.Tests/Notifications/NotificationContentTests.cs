using Heimdall.Core.Models;
using Heimdall.Core.Notifications;
using Shouldly;
using static Heimdall.Tests.TestSupport.RunBuilder;

namespace Heimdall.Tests.Notifications;

public class NotificationContentTests
{
    [Fact]
    public void Broke_title_names_the_repo_and_workflow()
    {
        var (title, _) = NotificationContent.Format(new NotificationPayload(NotificationKind.Broke, Run(RunStatus.Failure)));
        title.ShouldBe("demo: CI broke");
    }

    [Fact]
    public void Recovered_title_reads_recovered()
    {
        var (title, _) = NotificationContent.Format(new NotificationPayload(NotificationKind.Recovered, Run(RunStatus.Success)));
        title.ShouldBe("demo: CI recovered");
    }

    [Fact]
    public void Released_title_reads_shipped()
    {
        var (title, _) = NotificationContent.Format(new NotificationPayload(NotificationKind.Released, Run(RunStatus.Success, workflow: "CD")));
        title.ShouldBe("demo: CD shipped");
    }

    [Fact]
    public void Body_uses_the_branch_when_there_is_no_pull_request()
    {
        var (_, body) = NotificationContent.Format(new NotificationPayload(NotificationKind.Broke, Run(RunStatus.Failure, branch: "main", actor: "alice")));
        body.ShouldBe("octo/demo · main · alice");
    }

    [Fact]
    public void Body_prefers_the_pull_request_number_when_present()
    {
        var run = Run(RunStatus.Failure, branch: "feature", actor: "alice", prNumbers: [42]);
        var (_, body) = NotificationContent.Format(new NotificationPayload(NotificationKind.Broke, run));
        body.ShouldBe("octo/demo · PR #42 · alice");
    }
}
