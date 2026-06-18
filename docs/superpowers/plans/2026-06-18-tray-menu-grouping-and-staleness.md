# Tray Menu Grouping + Stale-Pipeline Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the tray right-click menu group pipelines by repo (one health-dotted line per repo, expandable to its pipelines) and drop any pipeline whose latest run is more than 30 days old from tracking entirely.

**Architecture:** Two independent changes. (1) In the polling layer, evict pipelines older than 30 days from the tracked state map after pruning, so they leave the tray colour, notifications, and the menu snapshot together; the clock is injected via `TimeProvider` for testability. (2) In the UI layer, a new pure `TrayMenuModel` helper turns the pipeline snapshot into ordered, health-classified repo groups, and `HeimdallOrchestrator.RebuildMenu` renders each group as a `NativeMenuItem` submenu.

**Tech Stack:** C# / .NET 10, Avalonia (`NativeMenu`/`NativeMenuItem`/`TrayIcon`), xUnit + Shouldly tests.

## Global Constraints

- Target framework: `net10.0` (all projects).
- `StaleAfter = TimeSpan.FromDays(30)`; not user-configurable.
- Health precedence is **failure-first**, matching `PipelineStateMachine.Aggregate`: Failing > Running > Passing > Unknown.
- Dots: Failing `🔴`, Running `🟡`, Passing `🟢`, Unknown `⚪`. Text: `failing` / `running` / `passing` / `unknown`.
- Pipeline label inside a submenu: `{WorkflowName} · {HeadBranch} — {word}`. Repo header: `{dot} {owner}/{repo}`.
- Ordering: repos by health (Failing→Running→Passing→Unknown) then `owner/repo` alphabetical (ordinal, case-insensitive); pipelines within a repo by workflow name then branch (ordinal, case-insensitive).
- The repo list contains only **tray-counting** pipelines (`CountsTowardTray == true`). Announce-only pipelines (`CountsTowardTray == false`) are excluded from repos entirely and surfaced under "Recently announced".
- "Recently announced": a single expandable item below the separator, shown only when there is ≥1 announce-only pipeline; its submenu lists announce-only pipelines ordered by `LastRun.CreatedAt` descending, capped at `RecentlyAnnouncedLimit = 10`; each entry label is `{dot} {owner}/{repo} · {workflow} · {branch} — {word}` (full owner/repo prefix), click opens `HtmlUrl`; the parent line has no click action.
- The macOS native-menu exporter requires the menu be rebuilt **in place** (`_menu.Items.Clear()` then re-add) — never reassign `_trayIcon.Menu`. Preserve this.
- No new production NuGet packages (`TimeProvider` is in the BCL). No new test packages (use a hand-written test `TimeProvider`).
- Comments explain *why*, not *what* (per repo convention).

---

### Task 1: Evict stale pipelines in the polling layer

**Files:**
- Modify: `src/Heimdall.Core/Polling/PollingService.cs`
- Create: `src/Heimdall.Tests/TestSupport/TestTimeProvider.cs`
- Modify: `src/Heimdall.Tests/TestSupport/RunBuilder.cs`
- Test: `src/Heimdall.Tests/Polling/PipelineStalenessTests.cs`

**Interfaces:**
- Consumes: `PollingService.PollOnceAsync(AppSettings, CancellationToken)`; `PipelineStateMachine.Prune`; `PipelineState.LastRun.CreatedAt`; `FakeGitHubGateway.OnGetRuns` (in `Heimdall.Tests/TestSupport`); `RunBuilder.Run(...)`.
- Produces:
  - `PollingService(IGitHubGateway gateway, RelevanceEngine engine, TimeProvider? timeProvider = null)` — new optional 3rd param.
  - `TestTimeProvider(DateTimeOffset now) : TimeProvider` with mutable `DateTimeOffset Now { get; set; }`.
  - `RunBuilder.Run(..., DateTimeOffset? createdAt = null)` — new optional trailing param defaulting to "now".

- [ ] **Step 1: Add the test clock helper**

Create `src/Heimdall.Tests/TestSupport/TestTimeProvider.cs`:

```csharp
namespace Heimdall.Tests.TestSupport;

/// <summary>A controllable <see cref="TimeProvider"/> for driving staleness logic deterministically.</summary>
internal sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
```

- [ ] **Step 2: Give `RunBuilder` a recent default `CreatedAt`**

In `src/Heimdall.Tests/TestSupport/RunBuilder.cs`, add a trailing optional parameter and use it. Existing tests pass nothing, so they get a fresh timestamp (and stop being treated as 56 years stale once the filter lands).

Change the signature's last parameter line from:

```csharp
        IReadOnlyList<string>? prAuthors = null) =>
```

to:

```csharp
        IReadOnlyList<string>? prAuthors = null,
        DateTimeOffset? createdAt = null) =>
```

and change the final record argument from:

```csharp
            CreatedAt: DateTimeOffset.UnixEpoch);
```

to:

```csharp
            CreatedAt: createdAt ?? DateTimeOffset.UtcNow);
```

- [ ] **Step 3: Write the failing staleness tests**

Create `src/Heimdall.Tests/Polling/PipelineStalenessTests.cs`:

```csharp
using Heimdall.Core.Models;
using Heimdall.Core.Polling;
using Heimdall.Core.Rules;
using Heimdall.Core.Settings;
using Heimdall.Tests.TestSupport;
using Shouldly;
using static Heimdall.Tests.TestSupport.RunBuilder;

namespace Heimdall.Tests.Polling;

public class PipelineStalenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    private static AppSettings Settings() => new(
        Repos: [new RepoConfig("octo", "demo", "main")],
        Identity: new Identity("alice"),
        RuleToggles: new Dictionary<string, bool> { [TriggeredByMeRule.RuleId] = true },
        PollIntervalSeconds: 60,
        NotificationsEnabled: true);

    private static PollingService NewService(FakeGitHubGateway gateway, TimeProvider clock) =>
        new(gateway, new RelevanceEngine(StandardRules.All), clock);

    [Fact]
    public async Task Pipelines_older_than_the_window_are_excluded()
    {
        var gateway = new FakeGitHubGateway
        {
            OnGetRuns = _ => [Run(RunStatus.Failure, actor: "alice", createdAt: Now.AddDays(-31))]
        };
        var service = NewService(gateway, new TestTimeProvider(Now));
        TrayStatus? tray = null;
        IReadOnlyList<PipelineState>? snapshot = null;
        service.Aggregate += s => tray = s;
        service.Snapshot += s => snapshot = s;

        await service.PollOnceAsync(Settings(), default);

        snapshot.ShouldNotBeNull();
        snapshot.ShouldBeEmpty();
        tray.ShouldBe(TrayStatus.Green);
    }

    [Fact]
    public async Task Pipelines_within_the_window_are_kept()
    {
        var gateway = new FakeGitHubGateway
        {
            OnGetRuns = _ => [Run(RunStatus.Success, actor: "alice", createdAt: Now.AddDays(-29))]
        };
        var service = NewService(gateway, new TestTimeProvider(Now));
        IReadOnlyList<PipelineState>? snapshot = null;
        service.Snapshot += s => snapshot = s;

        await service.PollOnceAsync(Settings(), default);

        snapshot.ShouldNotBeNull();
        snapshot.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_line_is_evicted_once_it_ages_out_despite_prune_retention()
    {
        var start = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new TestTimeProvider(start);
        var gateway = new FakeGitHubGateway
        {
            OnGetRuns = _ => [Run(RunStatus.Failure, actor: "alice", createdAt: start)]
        };
        var service = NewService(gateway, clock);
        TrayStatus? tray = null;
        IReadOnlyList<PipelineState>? snapshot = null;
        service.Aggregate += s => tray = s;
        service.Snapshot += s => snapshot = s;

        await service.PollOnceAsync(Settings(), default); // tracked while fresh
        tray.ShouldBe(TrayStatus.Red);

        // Branch goes quiet (no runs) and wall-clock advances past the window.
        gateway.OnGetRuns = _ => [];
        clock.Now = start.AddDays(31);
        await service.PollOnceAsync(Settings(), default);

        snapshot.ShouldNotBeNull();
        snapshot.ShouldBeEmpty();
        tray.ShouldBe(TrayStatus.Green);
    }
}
```

- [ ] **Step 4: Run the new tests to verify they fail**

Run: `dotnet test src/Heimdall.Tests/Heimdall.Tests.csproj --filter FullyQualifiedName~PipelineStalenessTests`
Expected: FAILS to compile — `PollingService` has no 3-arg constructor yet (`CS1729`/`CS7036`).

- [ ] **Step 5: Add the clock and staleness eviction to `PollingService`**

In `src/Heimdall.Core/Polling/PollingService.cs`:

Change the class declaration from:

```csharp
public sealed class PollingService(IGitHubGateway gateway, RelevanceEngine engine)
{
    private readonly IGitHubGateway _gateway = gateway;
    private readonly RelevanceEngine _engine = engine;
    private Dictionary<PipelineKey, PipelineState> _states = new();
```

to:

```csharp
public sealed class PollingService(IGitHubGateway gateway, RelevanceEngine engine, TimeProvider? timeProvider = null)
{
    // Pipelines whose latest run is older than this are dropped from tracking entirely — stale clutter
    // that should not colour the tray, notify, or appear in the menu.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

    private readonly IGitHubGateway _gateway = gateway;
    private readonly RelevanceEngine _engine = engine;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private Dictionary<PipelineKey, PipelineState> _states = new();
```

Then in `PollOnceAsync`, replace the prune-and-publish trio. Change:

```csharp
            _states = new Dictionary<PipelineKey, PipelineState>(PipelineStateMachine.Prune(_states, seen));
            Aggregate?.Invoke(PipelineStateMachine.Aggregate(_states.Values.Where(s => s.CountsTowardTray), connected: true));
            Snapshot?.Invoke(_states.Values.ToList());
```

to:

```csharp
            // Prune retains failing lines even when unseen (so a recovery can still notify); age-based
            // eviction is layered on here, where the cycle clock lives, to finally drop stale pipelines.
            var now = _time.GetUtcNow();
            _states = PipelineStateMachine.Prune(_states, seen)
                .Where(kv => now - kv.Value.LastRun.CreatedAt <= StaleAfter)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            Aggregate?.Invoke(PipelineStateMachine.Aggregate(_states.Values.Where(s => s.CountsTowardTray), connected: true));
            Snapshot?.Invoke(_states.Values.ToList());
```

- [ ] **Step 6: Run the new tests to verify they pass**

Run: `dotnet test src/Heimdall.Tests/Heimdall.Tests.csproj --filter FullyQualifiedName~PipelineStalenessTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Run the full core test suite to confirm no regressions**

Run: `dotnet test src/Heimdall.Tests/Heimdall.Tests.csproj`
Expected: PASS (all existing polling/announce/pruning tests still green — the `RunBuilder` default now resolves to `UtcNow`, so existing runs are fresh).

- [ ] **Step 8: Commit**

```bash
git add src/Heimdall.Core/Polling/PollingService.cs src/Heimdall.Tests/TestSupport/TestTimeProvider.cs src/Heimdall.Tests/TestSupport/RunBuilder.cs src/Heimdall.Tests/Polling/PipelineStalenessTests.cs
git commit -m "feat: drop pipelines idle for 30+ days from tracking"
```

---

### Task 2: `TrayMenuModel` — pure grouping/health/ordering helper

**Files:**
- Create: `src/Heimdall/TrayMenuModel.cs`
- Test: `src/Heimdall.UiTests/TrayMenuModelTests.cs`

**Interfaces:**
- Consumes: `Heimdall.Core.Models.PipelineState`, `PipelineKey` (`Owner`, `Repo`, `HeadBranch`), `RunRecord.WorkflowName`/`HtmlUrl`, `RunStatus`.
- Produces:
  - `internal sealed record TrayMenuEntry(string Dot, string Label, string Url)`
  - `internal sealed record TrayMenuRepoGroup(string Header, IReadOnlyList<TrayMenuEntry> Pipelines)`
  - `internal static IReadOnlyList<TrayMenuRepoGroup> TrayMenuModel.Build(IReadOnlyList<PipelineState> pipelines)`

- [ ] **Step 1: Write the failing tests**

Create `src/Heimdall.UiTests/TrayMenuModelTests.cs`:

```csharp
using Heimdall;
using Heimdall.Core.Models;
using Shouldly;

namespace Heimdall.UiTests;

public class TrayMenuModelTests
{
    private static PipelineState Pipeline(
        string owner, string repo, string branch, string workflow,
        RunStatus settled, bool inProgress = false, long workflowId = 1)
    {
        var run = new RunRecord(
            RunId: 1, WorkflowId: workflowId, WorkflowName: workflow,
            RepoOwner: owner, RepoName: repo, HeadBranch: branch, Event: "push",
            RunNumber: 1, Status: settled, TriggeringActorLogin: "alice",
            PullRequestNumbers: [], PullRequestAuthorLogins: [],
            HtmlUrl: $"https://github.com/{owner}/{repo}/actions/{workflow}/{branch}",
            CreatedAt: DateTimeOffset.UnixEpoch);
        return new PipelineState(new PipelineKey(owner, repo, workflowId, branch), settled, inProgress, 1, run);
    }

    [Fact]
    public void Groups_pipelines_under_their_repo_with_a_header_dot()
    {
        var groups = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "main", "build", RunStatus.Success),
            Pipeline("acme", "web", "main", "deploy", RunStatus.Success, workflowId: 2),
        ]);

        groups.Count.ShouldBe(1);
        groups[0].Header.ShouldBe("🟢 acme/web");
        groups[0].Pipelines.Count.ShouldBe(2);
    }

    [Fact]
    public void Repo_health_is_failure_first()
    {
        // A repo with a failing line and an in-progress line reads as failing (🔴), matching the tray.
        var groups = TrayMenuModel.Build(
        [
            Pipeline("acme", "api", "main", "ci", RunStatus.Failure),
            Pipeline("acme", "api", "dev", "ci", RunStatus.Success, inProgress: true, workflowId: 2),
        ]);

        groups.ShouldHaveSingleItem().Header.ShouldBe("🔴 acme/api");
    }

    [Fact]
    public void Repos_are_ordered_unhealthy_first_then_alphabetical()
    {
        var groups = TrayMenuModel.Build(
        [
            Pipeline("acme", "docs", "main", "ci", RunStatus.Success),
            Pipeline("acme", "api", "main", "ci", RunStatus.Failure),
            Pipeline("acme", "web", "main", "ci", RunStatus.Success),
            Pipeline("acme", "worker", "main", "ci", RunStatus.Success, inProgress: true),
        ]);

        groups.Select(g => g.Header).ShouldBe(
        [
            "🔴 acme/api",     // failing
            "🟡 acme/worker",  // running
            "🟢 acme/docs",    // passing, alphabetical
            "🟢 acme/web",
        ]);
    }

    [Fact]
    public void Pipelines_within_a_repo_are_ordered_by_workflow_then_branch()
    {
        var groups = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "release", "deploy", RunStatus.Success, workflowId: 2),
            Pipeline("acme", "web", "main", "deploy", RunStatus.Success, workflowId: 2),
            Pipeline("acme", "web", "main", "build", RunStatus.Success, workflowId: 1),
        ]);

        groups.ShouldHaveSingleItem().Pipelines.Select(p => p.Label).ShouldBe(
        [
            "build · main — passing",
            "deploy · main — passing",
            "deploy · release — passing",
        ]);
    }

    [Theory]
    [InlineData(RunStatus.Failure, false, "🔴", "failing")]
    [InlineData(RunStatus.Success, true, "🟡", "running")]
    [InlineData(RunStatus.Success, false, "🟢", "passing")]
    [InlineData(RunStatus.Unknown, false, "⚪", "unknown")]
    public void Maps_each_state_to_its_dot_and_word(RunStatus settled, bool inProgress, string dot, string word)
    {
        var entry = TrayMenuModel
            .Build([Pipeline("acme", "web", "main", "ci", settled, inProgress)])
            .ShouldHaveSingleItem()
            .Pipelines.ShouldHaveSingleItem();

        entry.Dot.ShouldBe(dot);
        entry.Label.ShouldEndWith($"— {word}");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Heimdall.UiTests/Heimdall.UiTests.csproj --filter FullyQualifiedName~TrayMenuModelTests`
Expected: FAILS to compile — `TrayMenuModel`/`TrayMenuEntry`/`TrayMenuRepoGroup` do not exist (`CS0103`).

- [ ] **Step 3: Implement `TrayMenuModel`**

Create `src/Heimdall/TrayMenuModel.cs`:

```csharp
using Heimdall.Core.Models;

namespace Heimdall;

/// <summary>One pipeline line inside a repo's submenu.</summary>
internal sealed record TrayMenuEntry(string Dot, string Label, string Url);

/// <summary>A repo's top-level menu line plus the pipelines revealed when it is expanded.</summary>
internal sealed record TrayMenuRepoGroup(string Header, IReadOnlyList<TrayMenuEntry> Pipelines);

/// <summary>
/// Turns a pipeline snapshot into health-classified, ordered repo groups for the tray menu.
/// Health is failure-first to stay consistent with the tray icon (see <see cref="PipelineStateMachine"/>
/// aggregation): a repo is as unhealthy as its worst pipeline.
/// </summary>
internal static class TrayMenuModel
{
    // Ordered worst-first so the enum value doubles as the sort key and the repo aggregate is a Min().
    private enum Health { Failing, Running, Passing, Unknown }

    public static IReadOnlyList<TrayMenuRepoGroup> Build(IReadOnlyList<PipelineState> pipelines) =>
        pipelines
            .GroupBy(p => $"{p.Key.Owner}/{p.Key.Repo}")
            .Select(group => (
                Slug: group.Key,
                Health: group.Select(Classify).Min(),
                Entries: group
                    .OrderBy(p => p.LastRun.WorkflowName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Key.HeadBranch, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new TrayMenuEntry(
                        Dot(Classify(p)),
                        $"{p.LastRun.WorkflowName} · {p.Key.HeadBranch} — {Word(Classify(p))}",
                        p.LastRun.HtmlUrl))
                    .ToList()))
            .OrderBy(g => g.Health)
            .ThenBy(g => g.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TrayMenuRepoGroup($"{Dot(g.Health)} {g.Slug}", g.Entries))
            .ToList();

    private static Health Classify(PipelineState pipeline) => pipeline switch
    {
        { LastSettledStatus: RunStatus.Failure } => Health.Failing,
        { InProgress: true } => Health.Running,
        { LastSettledStatus: RunStatus.Success } => Health.Passing,
        _ => Health.Unknown,
    };

    private static string Dot(Health health) => health switch
    {
        Health.Failing => "🔴",
        Health.Running => "🟡",
        Health.Passing => "🟢",
        _ => "⚪",
    };

    private static string Word(Health health) => health switch
    {
        Health.Failing => "failing",
        Health.Running => "running",
        Health.Passing => "passing",
        _ => "unknown",
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Heimdall.UiTests/Heimdall.UiTests.csproj --filter FullyQualifiedName~TrayMenuModelTests`
Expected: PASS (all `TrayMenuModelTests`).

- [ ] **Step 5: Commit**

```bash
git add src/Heimdall/TrayMenuModel.cs src/Heimdall.UiTests/TrayMenuModelTests.cs
git commit -m "feat: add tray menu model grouping pipelines by repo health"
```

---

### Task 3: Render repo submenus in `HeimdallOrchestrator.RebuildMenu`

**Files:**
- Modify: `src/Heimdall/HeimdallOrchestrator.cs` (`RebuildMenu` at ~line 132; remove the now-unused `Describe` at ~line 163)

**Interfaces:**
- Consumes: `TrayMenuModel.Build(...)`, `TrayMenuRepoGroup`, `TrayMenuEntry` (Task 2); `Shell.OpenUrl(string)`; Avalonia `NativeMenu`, `NativeMenuItem`, `NativeMenuItemSeparator`.
- Produces: no new public surface (private rendering change).

- [ ] **Step 1: Rewrite the pipeline section of `RebuildMenu`**

In `src/Heimdall/HeimdallOrchestrator.cs`, replace the body from `_menu.Items.Clear();` through the end of the pipeline-rendering block. Change:

```csharp
        _menu.Items.Clear();

        if (pipelines.Count == 0)
        {
            _menu.Items.Add(new NativeMenuItem("No pipelines yet") { IsEnabled = false });
        }
        else
        {
            foreach (var pipeline in pipelines.OrderBy(p => p.Key.Repo).ThenBy(p => p.Key.HeadBranch))
            {
                var label = $"{pipeline.Key.Owner}/{pipeline.Key.Repo} · {pipeline.LastRun.WorkflowName} · {pipeline.Key.HeadBranch} — {Describe(pipeline)}";
                var item = new NativeMenuItem(label);
                var url = pipeline.LastRun.HtmlUrl;
                item.Click += (_, _) => Shell.OpenUrl(url);
                _menu.Items.Add(item);
            }
        }
```

to:

```csharp
        _menu.Items.Clear();

        var groups = TrayMenuModel.Build(pipelines);
        if (groups.Count == 0)
        {
            _menu.Items.Add(new NativeMenuItem("No pipelines yet") { IsEnabled = false });
        }
        else
        {
            foreach (var group in groups)
            {
                var submenu = new NativeMenu();
                foreach (var entry in group.Pipelines)
                {
                    var item = new NativeMenuItem($"{entry.Dot} {entry.Label}");
                    var url = entry.Url;
                    item.Click += (_, _) => Shell.OpenUrl(url);
                    submenu.Items.Add(item);
                }

                _menu.Items.Add(new NativeMenuItem(group.Header) { Menu = submenu });
            }
        }
```

- [ ] **Step 2: Delete the now-unused `Describe` helper**

In the same file, remove:

```csharp
    private static string Describe(PipelineState pipeline) => pipeline switch
    {
        { InProgress: true } => "running",
        { LastSettledStatus: RunStatus.Failure } => "failing",
        { LastSettledStatus: RunStatus.Success } => "passing",
        _ => "unknown"
    };
```

- [ ] **Step 3: Build to verify it compiles and `Describe` had no other callers**

Run: `dotnet build src/Heimdall/Heimdall.csproj`
Expected: Build succeeds with no warnings about an unused/missing `Describe` (if the build fails with "Describe not found", restore it — but per the current file it is only used in `RebuildMenu`).

- [ ] **Step 4: Run the full UI test suite**

Run: `dotnet test src/Heimdall.UiTests/Heimdall.UiTests.csproj`
Expected: PASS (existing `ShellTests`, `TrayIconAssetsTests`, etc. plus `TrayMenuModelTests`).

- [ ] **Step 5: Manual smoke check**

The menu rendering itself is Avalonia native UI (not unit-tested). Verify by eye:

Run: `dotnet run --project src/Heimdall/Heimdall.csproj`
Expected: tray icon appears; right-click shows one dotted line per repo (unhealthy first), each expandable to its pipelines; clicking a pipeline opens its run URL in the browser; Settings…/Quit still present below the separator. (If you cannot run a tray app in this environment, note it and rely on the test suite + code review instead.)

- [ ] **Step 6: Commit**

```bash
git add src/Heimdall/HeimdallOrchestrator.cs
git commit -m "feat: group tray menu by repo with expandable pipeline submenus"
```

---

### Task 4: Recently-announced submenu + exclude announce-only from repo dots

**Files:**
- Modify: `src/Heimdall/TrayMenuModel.cs`
- Modify: `src/Heimdall.UiTests/TrayMenuModelTests.cs`
- Modify: `src/Heimdall/HeimdallOrchestrator.cs` (`RebuildMenu`)
- Modify (tidy): `src/Heimdall.Tests/Polling/PipelineStalenessTests.cs`

**Interfaces:**
- Consumes: `PipelineState.CountsTowardTray` (bool; `true` = drives tray colour, `false` = announce-only), `PipelineState.LastRun.CreatedAt`, `Shell.OpenUrl`.
- Produces (replaces Task 2's return shape):
  - `internal sealed record TrayMenu(IReadOnlyList<TrayMenuRepoGroup> Repos, IReadOnlyList<TrayMenuEntry> RecentlyAnnounced)`
  - `TrayMenuModel.Build(IReadOnlyList<PipelineState>)` now returns `TrayMenu` (was `IReadOnlyList<TrayMenuRepoGroup>`).
  - `TrayMenuEntry` and `TrayMenuRepoGroup` unchanged.

Background: `PipelineState.CountsTowardTray` defaults to `true`; `PollingService` sets it `false` for announce-only pipelines (`_states[key] = state with { CountsTowardTray = isRelevant }`). The repo list must use only tray-counting pipelines so repo dots match the tray icon; announce-only pipelines move to "Recently announced".

- [ ] **Step 1: Update the tests (TDD — change return shape + add announce coverage)**

Replace the body of `src/Heimdall.UiTests/TrayMenuModelTests.cs` with (the `Pipeline` helper gains `countsTowardTray` and `createdAt`; existing assertions move from the bare list to `.Repos`; new announce tests added):

```csharp
using Heimdall;
using Heimdall.Core.Models;
using Shouldly;

namespace Heimdall.UiTests;

public class TrayMenuModelTests
{
    private static PipelineState Pipeline(
        string owner, string repo, string branch, string workflow,
        RunStatus settled, bool inProgress = false, long workflowId = 1,
        bool countsTowardTray = true, DateTimeOffset? createdAt = null)
    {
        var run = new RunRecord(
            RunId: 1, WorkflowId: workflowId, WorkflowName: workflow,
            RepoOwner: owner, RepoName: repo, HeadBranch: branch, Event: "push",
            RunNumber: 1, Status: settled, TriggeringActorLogin: "alice",
            PullRequestNumbers: [], PullRequestAuthorLogins: [],
            HtmlUrl: $"https://github.com/{owner}/{repo}/actions/{workflow}/{branch}",
            CreatedAt: createdAt ?? DateTimeOffset.UnixEpoch);
        return new PipelineState(new PipelineKey(owner, repo, workflowId, branch), settled, inProgress, 1, run)
        {
            CountsTowardTray = countsTowardTray,
        };
    }

    [Fact]
    public void Groups_pipelines_under_their_repo_with_a_header_dot()
    {
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "main", "build", RunStatus.Success),
            Pipeline("acme", "web", "main", "deploy", RunStatus.Success, workflowId: 2),
        ]);

        menu.Repos.Count.ShouldBe(1);
        menu.Repos[0].Header.ShouldBe("🟢 acme/web");
        menu.Repos[0].Pipelines.Count.ShouldBe(2);
        menu.RecentlyAnnounced.ShouldBeEmpty();
    }

    [Fact]
    public void Repo_health_is_failure_first()
    {
        // A repo with a failing line and an in-progress line reads as failing (🔴), matching the tray.
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "api", "main", "ci", RunStatus.Failure),
            Pipeline("acme", "api", "dev", "ci", RunStatus.Success, inProgress: true, workflowId: 2),
        ]);

        menu.Repos.ShouldHaveSingleItem().Header.ShouldBe("🔴 acme/api");
    }

    [Fact]
    public void Repos_are_ordered_unhealthy_first_then_alphabetical()
    {
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "docs", "main", "ci", RunStatus.Success),
            Pipeline("acme", "api", "main", "ci", RunStatus.Failure),
            Pipeline("acme", "web", "main", "ci", RunStatus.Success),
            Pipeline("acme", "worker", "main", "ci", RunStatus.Success, inProgress: true),
        ]);

        menu.Repos.Select(g => g.Header).ShouldBe(
        [
            "🔴 acme/api",     // failing
            "🟡 acme/worker",  // running
            "🟢 acme/docs",    // passing, alphabetical
            "🟢 acme/web",
        ]);
    }

    [Fact]
    public void Pipelines_within_a_repo_are_ordered_by_workflow_then_branch()
    {
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "release", "deploy", RunStatus.Success, workflowId: 2),
            Pipeline("acme", "web", "main", "deploy", RunStatus.Success, workflowId: 2),
            Pipeline("acme", "web", "main", "build", RunStatus.Success, workflowId: 1),
        ]);

        menu.Repos.ShouldHaveSingleItem().Pipelines.Select(p => p.Label).ShouldBe(
        [
            "build · main — passing",
            "deploy · main — passing",
            "deploy · release — passing",
        ]);
    }

    [Theory]
    [InlineData(RunStatus.Failure, false, "🔴", "failing")]
    [InlineData(RunStatus.Success, true, "🟡", "running")]
    [InlineData(RunStatus.Success, false, "🟢", "passing")]
    [InlineData(RunStatus.Unknown, false, "⚪", "unknown")]
    public void Maps_each_state_to_its_dot_and_word(RunStatus settled, bool inProgress, string dot, string word)
    {
        var entry = TrayMenuModel
            .Build([Pipeline("acme", "web", "main", "ci", settled, inProgress)])
            .Repos.ShouldHaveSingleItem()
            .Pipelines.ShouldHaveSingleItem();

        entry.Dot.ShouldBe(dot);
        entry.Label.ShouldEndWith($"— {word}");
    }

    [Fact]
    public void Empty_input_produces_no_repos_and_no_announcements()
    {
        var menu = TrayMenuModel.Build([]);

        menu.Repos.ShouldBeEmpty();
        menu.RecentlyAnnounced.ShouldBeEmpty();
    }

    [Fact]
    public void Announce_only_pipelines_are_excluded_from_repos_and_listed_under_recently_announced()
    {
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "main", "ci", RunStatus.Success),
            Pipeline("acme", "api", "main", "release", RunStatus.Failure, countsTowardTray: false),
        ]);

        menu.Repos.ShouldHaveSingleItem().Header.ShouldBe("🟢 acme/web");
        var announced = menu.RecentlyAnnounced.ShouldHaveSingleItem();
        announced.Dot.ShouldBe("🔴");
        announced.Label.ShouldBe("acme/api · release · main — failing");
    }

    [Fact]
    public void Announce_only_failure_does_not_redden_its_repo_dot()
    {
        // An announce-only failing run in the same repo must not affect the repo's health dot.
        var menu = TrayMenuModel.Build(
        [
            Pipeline("acme", "web", "main", "ci", RunStatus.Success),
            Pipeline("acme", "web", "main", "release", RunStatus.Failure, workflowId: 2, countsTowardTray: false),
        ]);

        menu.Repos.ShouldHaveSingleItem().Header.ShouldBe("🟢 acme/web");
    }

    [Fact]
    public void Recently_announced_is_newest_first_and_capped_at_ten()
    {
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var pipelines = Enumerable.Range(0, 12)
            .Select(i => Pipeline(
                "acme", "repo" + i, "main", "release", RunStatus.Success,
                countsTowardTray: false, createdAt: epoch.AddDays(i)))
            .ToList();

        var announced = TrayMenuModel.Build(pipelines).RecentlyAnnounced;

        announced.Count.ShouldBe(10);
        announced[0].Label.ShouldBe("acme/repo11 · release · main — passing"); // newest
        announced[9].Label.ShouldBe("acme/repo2 · release · main — passing");  // 10th newest
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Heimdall.UiTests/Heimdall.UiTests.csproj --filter FullyQualifiedName~TrayMenuModelTests`
Expected: FAILS to compile — `Build(...)` still returns `IReadOnlyList<TrayMenuRepoGroup>`, so `.Repos` / `.RecentlyAnnounced` do not exist (`CS1061`).

- [ ] **Step 3: Update `TrayMenuModel` to return `TrayMenu`**

In `src/Heimdall/TrayMenuModel.cs`, add the `TrayMenu` record after the existing records:

```csharp
/// <summary>A repo's top-level menu line plus the pipelines revealed when it is expanded.</summary>
internal sealed record TrayMenuRepoGroup(string Header, IReadOnlyList<TrayMenuEntry> Pipelines);

/// <summary>The full tray menu model: health-grouped repos plus the recently-announced releases.</summary>
internal sealed record TrayMenu(IReadOnlyList<TrayMenuRepoGroup> Repos, IReadOnlyList<TrayMenuEntry> RecentlyAnnounced);
```

Add the limit constant inside the class (next to the `Health` enum):

```csharp
    // Ordered worst-first so the enum value doubles as the sort key and the repo aggregate is a Min().
    private enum Health { Failing, Running, Passing, Unknown }

    private const int RecentlyAnnouncedLimit = 10;
```

Replace the `Build` method (currently an expression-bodied member returning the list) with:

```csharp
    public static TrayMenu Build(IReadOnlyList<PipelineState> pipelines)
    {
        var repos = pipelines
            .Where(p => p.CountsTowardTray)
            .GroupBy(p => $"{p.Key.Owner}/{p.Key.Repo}")
            .Select(group => (
                Slug: group.Key,
                Health: group.Select(Classify).Min(),
                Entries: group
                    .OrderBy(p => p.LastRun.WorkflowName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Key.HeadBranch, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new TrayMenuEntry(
                        Dot(Classify(p)),
                        $"{p.LastRun.WorkflowName} · {p.Key.HeadBranch} — {Word(Classify(p))}",
                        p.LastRun.HtmlUrl))
                    .ToList()))
            .OrderBy(g => g.Health)
            .ThenBy(g => g.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TrayMenuRepoGroup($"{Dot(g.Health)} {g.Slug}", g.Entries))
            .ToList();

        // Announce-only pipelines are tracked for release notifications but excluded from the tray
        // colour, so they live in their own recency-ordered list rather than skewing repo health.
        var recentlyAnnounced = pipelines
            .Where(p => !p.CountsTowardTray)
            .OrderByDescending(p => p.LastRun.CreatedAt)
            .ThenBy(p => p.LastRun.HtmlUrl, StringComparer.Ordinal)
            .Take(RecentlyAnnouncedLimit)
            .Select(p => new TrayMenuEntry(
                Dot(Classify(p)),
                $"{p.Key.Owner}/{p.Key.Repo} · {p.LastRun.WorkflowName} · {p.Key.HeadBranch} — {Word(Classify(p))}",
                p.LastRun.HtmlUrl))
            .ToList();

        return new TrayMenu(repos, recentlyAnnounced);
    }
```

(The `Classify`, `Dot`, and `Word` helpers below are unchanged.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Heimdall.UiTests/Heimdall.UiTests.csproj --filter FullyQualifiedName~TrayMenuModelTests`
Expected: PASS (all `TrayMenuModelTests`, including the new announce tests).

- [ ] **Step 5: Render the recently-announced submenu in `RebuildMenu`**

In `src/Heimdall/HeimdallOrchestrator.cs`, replace the `RebuildMenu` body (everything between `_menu.Items.Clear();` and the final Settings/Quit block) so it consumes `TrayMenu`, factors the submenu-building into a local helper (avoids duplicating the item+click logic), and adds the "Recently announced" item after the separator. Replace:

```csharp
        _menu.Items.Clear();

        var groups = TrayMenuModel.Build(pipelines);
        if (groups.Count == 0)
        {
            _menu.Items.Add(new NativeMenuItem("No pipelines yet") { IsEnabled = false });
        }
        else
        {
            foreach (var group in groups)
            {
                var submenu = new NativeMenu();
                foreach (var entry in group.Pipelines)
                {
                    var item = new NativeMenuItem($"{entry.Dot} {entry.Label}");
                    var url = entry.Url;
                    item.Click += (_, _) => Shell.OpenUrl(url);
                    submenu.Items.Add(item);
                }

                _menu.Items.Add(new NativeMenuItem(group.Header) { Menu = submenu });
            }
        }

        _menu.Items.Add(new NativeMenuItemSeparator());

        var settings = new NativeMenuItem("Settings…");
```

with:

```csharp
        _menu.Items.Clear();

        var menu = TrayMenuModel.Build(pipelines);
        if (menu.Repos.Count == 0)
        {
            _menu.Items.Add(new NativeMenuItem("No pipelines yet") { IsEnabled = false });
        }
        else
        {
            foreach (var group in menu.Repos)
                _menu.Items.Add(new NativeMenuItem(group.Header) { Menu = SubmenuOf(group.Pipelines) });
        }

        _menu.Items.Add(new NativeMenuItemSeparator());

        // Announce-only releases live below the separator and only when there are any to show.
        if (menu.RecentlyAnnounced.Count > 0)
            _menu.Items.Add(new NativeMenuItem("Recently announced") { Menu = SubmenuOf(menu.RecentlyAnnounced) });

        var settings = new NativeMenuItem("Settings…");
```

Then add this local helper inside `RebuildMenu`, after the Quit block (still inside the method):

```csharp
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();
        _menu.Items.Add(quit);

        static NativeMenu SubmenuOf(IReadOnlyList<TrayMenuEntry> entries)
        {
            var submenu = new NativeMenu();
            foreach (var entry in entries)
            {
                var item = new NativeMenuItem($"{entry.Dot} {entry.Label}");
                var url = entry.Url; // capture per-iteration so each item opens its own run
                item.Click += (_, _) => Shell.OpenUrl(url);
                submenu.Items.Add(item);
            }

            return submenu;
        }
```

- [ ] **Step 6: Tidy — pin the staleness boundary (clears a logged Minor from Task 1)**

In `src/Heimdall.Tests/Polling/PipelineStalenessTests.cs`, add a test pinning the exact-30-days boundary (the predicate is `<= StaleAfter`, so exactly 30 days is kept):

```csharp
    [Fact]
    public async Task A_pipeline_exactly_at_the_window_boundary_is_kept()
    {
        var gateway = new FakeGitHubGateway
        {
            OnGetRuns = _ => [Run(RunStatus.Success, actor: "alice", createdAt: Now.AddDays(-30))]
        };
        var service = NewService(gateway, new TestTimeProvider(Now));
        IReadOnlyList<PipelineState>? snapshot = null;
        service.Snapshot += s => snapshot = s;

        await service.PollOnceAsync(Settings(), default);

        snapshot.ShouldNotBeNull();
        snapshot.Count.ShouldBe(1);
    }
```

- [ ] **Step 7: Build and run both suites**

Run: `dotnet build src/Heimdall/Heimdall.csproj`
Expected: succeeds, no warnings.

Run: `dotnet test Heimdall.slnx`
Expected: PASS (all core + UI tests, including the new announce and boundary tests).

- [ ] **Step 8: Manual smoke check (best effort)**

If a tray GUI can be launched here: `dotnet run --project src/Heimdall/Heimdall.csproj` and confirm announce-only workflows no longer affect repo dots and appear under a "Recently announced" submenu below the separator. If the environment has no display, note that and rely on the suites + review.

- [ ] **Step 9: Commit**

```bash
git add src/Heimdall/TrayMenuModel.cs src/Heimdall.UiTests/TrayMenuModelTests.cs src/Heimdall/HeimdallOrchestrator.cs src/Heimdall.Tests/Polling/PipelineStalenessTests.cs
git commit -m "feat: surface announce-only workflows in a recently-announced submenu"
```

---

## Self-Review

**Spec coverage:**
- Menu grouped by repo with health dot + expandable submenu → Task 2 (`TrayMenuModel`) + Task 3 (`RebuildMenu`). ✓
- Failure-first health, dots, words, label/header formats → Task 2 + Global Constraints. ✓
- Ordering (repos unhealthy-first then alphabetical; pipelines workflow→branch) → Task 2 tests + Global Constraints. ✓
- Repo line expand-only, pipeline click opens URL → Task 3 (`group.Header` item has no `Click`; entries keep `Click`). ✓
- Empty state "No pipelines yet" preserved (now keyed off `Repos.Count == 0`) → Task 3, updated in Task 4. ✓
- In-place menu rebuild preserved (`_menu.Items.Clear()`, no reassignment) → Task 3, Task 4. ✓
- Announce-only excluded from repos so dots match the tray; "Recently announced" submenu below separator, 10-capped, newest-first, owner/repo-prefixed labels, shown only when non-empty → Task 4 (`TrayMenu` return shape, `RebuildMenu`). ✓
- 30-day eviction across tray colour + notifications + snapshot, clock injected, `StaleAfter` const → Task 1. ✓
- `RunBuilder` default fix so existing tests stay fresh → Task 1 Step 2 + Step 7. ✓
- Test clock without new package → Task 1 Step 1 (`TestTimeProvider`). ✓
- Out of scope (configurable threshold, relevance changes, settings/notification restructuring) → not touched. ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code; commands have expected output. ✓

**Type consistency:** `TrayMenuModel.Build` / `TrayMenuRepoGroup(Header, Pipelines)` / `TrayMenuEntry(Dot, Label, Url)` used identically in Task 2 (definition + tests) and Task 3 (consumption). `PollingService` 3-arg constructor used in Task 1 tests matches the definition. `TestTimeProvider.Now` mutated in Task 1 Step 3 matches its definition in Step 1. ✓
