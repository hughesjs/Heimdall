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
Recently announced  ▸
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
  tray-counting (non-stale) pipelines exist.
- The Settings…/Quit chrome and the separator are unchanged.
- The repo list contains only **tray-counting** pipelines (`CountsTowardTray`).
  Announce-only pipelines (tracked for release notifications but excluded from the
  tray colour) are surfaced separately under "Recently announced" (below), so a repo
  dot can never disagree with the tray icon.

### Recently announced

Announce-only pipelines (`CountsTowardTray == false`) are the workflows a user adds
to a repo's announce list that are not otherwise relevant to them — they fire
"released/shipped" notifications but must not colour the tray. They do not belong in
the health list, so they are collected into a single expandable **"Recently
announced"** item placed *below the separator*, above Settings…:

```
─────────────
Recently announced  ▸   🟢 acme/api · release · main — passing
                        🔴 acme/web · deploy · main — failing
Settings…
Quit
```

- The "Recently announced" parent line only expands (no click action) and is shown
  **only when there is at least one announce-only pipeline** — otherwise it is omitted
  entirely.
- Its submenu lists the most recent announce-only pipelines by `LastRun.CreatedAt`
  descending, capped at the 10 newest (`RecentlyAnnouncedLimit = 10`). The 30-day
  staleness eviction already applies upstream, so these are recent by construction.
- Each entry keeps the full `owner/repo` prefix (there is no parent repo here):
  `{dot} {owner}/{repo} · {workflow} · {branch} — {word}`, using the same `Classify`
  dot/word as the health list. Clicking opens its `HtmlUrl`.

### Repo health aggregation

A single `Classify(PipelineState)` function maps one pipeline to a health value,
using the same **failure-first** precedence as the existing tray `Aggregate`
(`PipelineStateMachine.Aggregate`), so repo dots, pipeline dots, and the tray icon
never disagree:

| Condition (checked in order)        | Health  | Dot | Text suffix |
|-------------------------------------|---------|-----|-------------|
| `LastSettledStatus == Failure`      | Failing | 🔴  | failing     |
| else `InProgress`                   | Running | 🟡  | running     |
| else `LastSettledStatus == Success` | Passing | 🟢  | passing     |
| otherwise                           | Unknown | ⚪  | unknown     |

A repo's health is the worst of its **tray-counting** pipelines' healths under the
precedence Failing > Running > Passing > Unknown (i.e. the minimum when the enum is
ordered `Failing=0 … Unknown=3`). The repo's dot and ordering group both come from
this. Announce-only pipelines are excluded from the repo entirely (see "Recently
announced"), which is what lets repo dots, pipeline dots, and the tray icon agree.

Note this is failure-first, unlike today's `Describe` (which checks `InProgress`
first). A pipeline that last failed and is now re-running therefore reads
`🔴 … — failing`, matching the red tray icon, rather than showing as running.

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

A post-`Prune` age eviction in `PollingService.PollOnceAsync`
(`src/Heimdall.Core/Polling/PollingService.cs`). Today the cycle ends with:

```csharp
_states = new Dictionary<PipelineKey, PipelineState>(PipelineStateMachine.Prune(_states, seen));
Aggregate?.Invoke(PipelineStateMachine.Aggregate(_states.Values.Where(s => s.CountsTowardTray), connected: true));
Snapshot?.Invoke(_states.Values.ToList());
```

Insert an age filter between `Prune` and the events:

```csharp
var pruned = PipelineStateMachine.Prune(_states, seen);
_states = pruned
    .Where(kv => now - kv.Value.LastRun.CreatedAt <= StaleAfter)
    .ToDictionary(kv => kv.Key, kv => kv.Value);
```

`now` is `_time.GetUtcNow()`, captured once at the top of the cycle. Because the
eviction runs on the final `_states` map, a stale pipeline never contributes to the
aggregate tray status and never appears in the menu snapshot.

**Why here and not at intake.** `PipelineStateMachine.Prune` intentionally retains
failing lines even when they are not seen in a cycle (so a later recovery can still
notify), and its doc comment states that age-based eviction "is layered on by the
polling service, which owns the cycle clock." A filter at run-intake
(`LatestTrackedPerKey`) would not remove a previously-tracked failure whose branch
has since gone quiet — `Prune` would keep it red indefinitely. Evicting after
`Prune` is the single chokepoint that actually removes stale lines and fulfils that
documented intent.

Notifications need no separate guard: runs only ever get newer, so a run cannot age
*into* relevance, and a first sighting of an already-stale run seeds silently (no
notification) before being evicted the same cycle.

### Clock injection

`PollingService` currently has the primary constructor
`(IGitHubGateway gateway, RelevanceEngine engine)`. Add an optional
`TimeProvider? timeProvider = null`, stored as `_time = timeProvider ?? TimeProvider.System`.
Staleness is measured against `_time.GetUtcNow()`, computed once per cycle in
`PollOnceAsync`. `TimeProvider` is a BCL type (no new production package).

Rationale: injecting the clock makes the 30-day boundary deterministically testable
via a tiny test `TimeProvider`, rather than depending on wall-clock `DateTimeOffset.UtcNow`.

### Constant

`private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);` on
`PollingService`. Not user-configurable (YAGNI).

## Menu implementation

All menu changes are contained in `HeimdallOrchestrator`
(`src/Heimdall/HeimdallOrchestrator.cs`), which already rebuilds the menu in place
on each `Snapshot` event (the in-place `_menu.Items.Clear()` + rebuild approach is
required by the macOS native-menu exporter and is preserved).

- A new pure static helper (`TrayMenuModel`) performs `Classify` + partitioning +
  grouping + health aggregation + ordering, taking `IReadOnlyList<PipelineState>` and
  returning a `TrayMenu` with two parts: `Repos` (the ordered repo groups, each a
  header string + its pipeline entries) and `RecentlyAnnounced` (the ordered,
  10-capped announce-only entries). Keeping it free of Avalonia types makes it
  unit-testable from `Heimdall.UiTests` (which already has `InternalsVisibleTo`).
- `RebuildMenu` renders that model: for each repo group, a parent `NativeMenuItem`
  with the health dot in its `Header` and a child `NativeMenu` of per-pipeline items
  (click → `Shell.OpenUrl(url)`); then the separator; then, only when
  `RecentlyAnnounced` is non-empty, a "Recently announced" parent `NativeMenuItem`
  with a child `NativeMenu` of its entries (click → `Shell.OpenUrl(url)`); then
  Settings…/Quit.
- The dot/word mappings live in `TrayMenuModel` and replace the old running-first
  `Describe`; both the dot and the text suffix derive from the one `Classify`.

The empty-state branch ("No pipelines yet", now keyed off `Repos.Count == 0`) and the
Settings/Quit construction are otherwise unchanged.

## Testing

### Polling (`src/Heimdall.Tests`)

- **Fixture fix:** `RunBuilder.Run` currently defaults `CreatedAt` to
  `DateTimeOffset.UnixEpoch` (1970). Under a real-clock staleness filter this would
  make every existing run ~56 years stale and break the polling suite. Add an
  optional `createdAt` parameter resolving to a recent value (e.g. `DateTimeOffset.UtcNow`
  when not supplied) so existing tests stay fresh by default.
- **Test clock:** a tiny `TestTimeProvider : TimeProvider` in `TestSupport` with a
  settable `Now` (overriding `GetUtcNow()`) — avoids a new NuGet dependency.
- **New staleness tests** (fixed `Now`, explicit `createdAt`):
  - A pipeline whose latest run is older than 30 days is excluded from the snapshot
    and does not affect the aggregate tray status.
  - A pipeline whose latest run is just within 30 days is retained.
  - A previously-tracked *failing* pipeline whose branch goes quiet is evicted once
    its run ages past 30 days (proves eviction beats `Prune`'s failing-line retention):
    advance `Now` between cycles and assert the snapshot empties and the tray greens.

### Menu grouping (pure helper)

Unit tests on the pure grouping/health/ordering helper:

- Pipelines grouped under the correct `owner/repo` (tray-counting only).
- Repo health precedence: failing > running > passing > unknown.
- Ordering: failing → running → passing → unknown, alphabetical within group.
- Status→dot mapping for each `RunStatus`/`InProgress` combination.
- Announce-only pipelines (`CountsTowardTray == false`) are excluded from `Repos` and
  do not affect any repo dot.
- `RecentlyAnnounced` contains only announce-only pipelines, ordered by
  `LastRun.CreatedAt` descending, capped at 10, with the `owner/repo`-prefixed label.

## Out of scope

- Making the 30-day threshold configurable.
- Changing the relevance rules or what counts toward the tray beyond the staleness
  filter.
- Restructuring the Settings window or notification behaviour.
