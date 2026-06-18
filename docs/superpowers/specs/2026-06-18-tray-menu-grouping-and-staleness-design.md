# Tray menu: group by repo + drop stale pipelines

## Problem

The tray right-click menu is too noisy. Every tracked pipeline is rendered as its
own flat, verbose line (`owner/repo · workflow · branch — status`), so a handful of
repos, branches, and workflows quickly fills the menu. The menu also keeps showing
pipelines whose workflows have not run in a long time, which is irrelevant clutter.

What the user wants:

1. A quick right-click that shows **which repos are healthy and which are not**.
2. The ability to **expand a repo** to see its individual pipelines.
3. Pipelines that **have not run in 30+ days filtered out entirely** — not just
   hidden from the menu, but excluded from tray colour and notifications too.

## Design overview

Two independent changes:

- **Polling layer** — filter out stale pipelines (latest run older than 30 days) so
  they never enter the tracked state map. This affects tray colour, notifications,
  pruning, and the menu snapshot uniformly.
- **Menu layer** — render the (already non-stale) pipelines grouped by repo, with a
  health dot per repo and the individual pipelines in a submenu.

### Menu structure

Top level is one item per repo, each a submenu. A coloured dot prefixes the repo's
aggregate health:

```
🔴 acme/api
🟡 acme/worker
🟢 acme/docs
🟢 acme/web
─────────────
Settings…
Quit
```

Expanding a repo reveals its pipelines, with the redundant `owner/repo` prefix
dropped (the repo is the parent), each pipeline also dot-prefixed for consistency:

```
🟢 acme/web  ▸   🟢 build · main — passing
                 🟢 deploy · main — passing
                 🟡 e2e · release/4.x — running
```

Behaviour:

- The repo line only expands; it has no click action.
- Each pipeline line keeps today's behaviour: clicking opens its `HtmlUrl`.
- Empty state ("No pipelines yet", disabled) is unchanged, and applies when no
  non-stale pipelines exist.
- The Settings…/Quit chrome and the separator are unchanged.

### Repo health aggregation

A repo's dot and ordering group are derived from its pipelines by worst-first
precedence:

| Condition (any pipeline in the repo)            | Dot | Group   |
|-------------------------------------------------|-----|---------|
| Any failing (`LastSettledStatus == Failure`)    | 🔴  | failing |
| Else any in progress (`InProgress`)             | 🟡  | running |
| Else all passing (`LastSettledStatus == Success`)| 🟢  | passing |
| Otherwise                                       | ⚪  | unknown |

The per-pipeline dot uses the same status→dot mapping applied to that single
pipeline (running if `InProgress`, else failing/passing/unknown from
`LastSettledStatus`), matching today's `Describe` wording for the text suffix.

### Ordering

Top-level repos are ordered by health group first (failing → running → passing →
unknown), then alphabetically by `owner/repo` within each group. Problems float to
the top; the coloured dots make state scannable. Pipelines within a repo's submenu
are ordered by workflow name, then branch — so the full ordering is repo → workflow
→ branch (the current flat menu sorts by repo then branch; with repo now the grouping
key, workflow leads inside the submenu).

## Staleness filter (polling layer)

### Rule

A pipeline is **stale** when its latest run's `CreatedAt` is more than `StaleAfter`
(`TimeSpan.FromDays(30)`) before the current time. Stale pipelines are filtered out
before they enter the tracked state map.

### Where

In `PollingService.LatestTrackedPerKey` (`src/Heimdall.Core/Polling/PollingService.cs`).
Today it selects the latest run per `PipelineKey`:

```csharp
.GroupBy(PipelineKey.For)
.Select(group => group.MaxBy(run => run.RunNumber)!);
```

Add a staleness filter after the latest run is chosen:

```csharp
.GroupBy(PipelineKey.For)
.Select(group => group.MaxBy(run => run.RunNumber)!)
.Where(run => now - run.CreatedAt <= StaleAfter);
```

`now` is threaded in from the caller (`PollOnceAsync`) so the method stays a pure
function of its inputs. Because this is upstream of `_states`, a stale pipeline:

- never contributes to the aggregate tray status,
- never fires a transition/announce notification,
- is pruned implicitly (it is simply never added), and
- never appears in the menu snapshot.

This is the single chokepoint that realises "filter out entirely".

### Clock injection

`PollingService` currently has the primary constructor
`(IGitHubGateway gateway, RelevanceEngine engine)`. Add an optional
`TimeProvider? timeProvider = null`, stored as `_time = timeProvider ?? TimeProvider.System`.
Staleness is measured against `_time.GetUtcNow()`, computed once per cycle in
`PollOnceAsync` and passed to `LatestTrackedPerKey`.

Rationale: a `FakeTimeProvider` makes the 30-day boundary deterministically testable,
rather than depending on wall-clock `DateTimeOffset.UtcNow`.

### Constant

`private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);` on
`PollingService`. Not user-configurable (YAGNI).

## Menu implementation

All menu changes are contained in `HeimdallOrchestrator`
(`src/Heimdall/HeimdallOrchestrator.cs`), which already rebuilds the menu in place
on each `Snapshot` event (the in-place `_menu.Items.Clear()` + rebuild approach is
required by the macOS native-menu exporter and is preserved).

- `RebuildMenu` groups the incoming pipelines by `owner/repo`, builds a parent
  `NativeMenuItem` per repo with the health dot in its `Header`, assigns a child
  `NativeMenu` to the parent's `.Menu` property to form the submenu, and adds the
  per-pipeline `NativeMenuItem`s (with click → `Shell.OpenUrl(url)`) to that child.
- A pure static helper performs grouping + health aggregation + ordering, taking
  `IReadOnlyList<PipelineState>` and returning the ordered repo groups with their
  computed health. Keeping it pure (no Avalonia types) makes it unit-testable.
- A small status→dot mapping (`🔴/🟡/🟢/⚪`) and the repo-health precedence live
  alongside the existing `Describe` helper. The text suffix continues to use
  `Describe`.

The empty-state branch (`pipelines.Count == 0`) and the Settings/Quit construction
are unchanged.

## Testing

### Polling (`src/Heimdall.Tests`)

- **Fixture fix:** `RunBuilder.Run` currently defaults `CreatedAt` to
  `DateTimeOffset.UnixEpoch` (1970). Under a real-clock staleness filter this would
  make every existing run ~56 years stale and break the polling suite. Add an
  optional `createdAt` parameter resolving to a recent value (e.g. `DateTimeOffset.UtcNow`
  when not supplied) so existing tests stay fresh by default.
- **New staleness tests** (using `FakeTimeProvider` set to a fixed instant and
  explicit `createdAt`):
  - A pipeline whose latest run is older than 30 days is excluded from the snapshot,
    does not affect the aggregate tray status, and fires no notification.
  - A pipeline whose latest run is exactly at / just within 30 days is retained.
  - A repo whose only pipeline is stale disappears; if all pipelines across all repos
    are stale, the snapshot is empty.

### Menu grouping (pure helper)

Unit tests on the pure grouping/health/ordering helper:

- Pipelines grouped under the correct `owner/repo`.
- Repo health precedence: failing > running > passing > unknown.
- Ordering: failing → running → passing → unknown, alphabetical within group.
- Status→dot mapping for each `RunStatus`/`InProgress` combination.

## Out of scope

- Making the 30-day threshold configurable.
- Changing the relevance rules or what counts toward the tray beyond the staleness
  filter.
- Restructuring the Settings window or notification behaviour.
