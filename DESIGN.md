# Heimdall — Design

Companion to `SPEC.md`. Records the agreed design of the five Core layers and the decisions behind them. No implementation yet — folders are scaffolded with `.gitkeep`.

## Architecture at a glance

```
PeriodicTimer (~60s)
  └─ PollingService          (Polling/)  — the spine
       ├─ IGitHubGateway     (GitHub/)   — run listing, ETag, PR-author enrichment, rate limit
       ├─ RelevanceEngine    (Rules/)    — OR of enabled IRelevanceRule, pure field comparisons
       └─ state map           ─ Dictionary<PipelineKey, PipelineState>, single writer
            └─ raises events: Transition / Aggregate / Snapshot
```

`Heimdall.Core` is Avalonia-free and raises plain events. The `Heimdall` app subscribes and marshals to the UI thread (`Dispatcher.UIThread.Post`) for the tray icon and notifications, and supplies the platform implementations of `ITokenStore` and `INotificationManager`.

---

## 1. State model (`Models/`, `Polling/`)

Status enum — nothing above the gateway touches Octokit types:

```csharp
public enum RunStatus { Success, Failure, InProgress, Unknown }
```
`Unknown` covers `cancelled`, `skipped`, `neutral`, `timed_out`, etc. **Only `Success`↔`Failure` fire notifications** — `Unknown` never triggers "broke" or "recovered".

```csharp
public record RunRecord(
    long RunId, long WorkflowId, string WorkflowName,
    string RepoOwner, string RepoName, string HeadBranch, string Event,
    int RunNumber, RunStatus Status, string TriggeringActorLogin,
    IReadOnlyList<int> PullRequestNumbers,
    IReadOnlyList<string> PullRequestAuthorLogins,   // enriched by the gateway, see §3
    string HtmlUrl, DateTimeOffset CreatedAt);

public readonly record struct PipelineKey(string Owner, string Repo, long WorkflowId, string HeadBranch);

public record PipelineState(
    PipelineKey Key,
    RunStatus LastSettledStatus,   // comparison anchor — Success/Failure/Unknown
    bool      InProgress,          // latest run queued/in_progress → drives amber
    long      LastRunId,
    RunRecord LastRun);            // notification content + tray menu
```

**Pipeline line = (repo, workflow, branch).** Per-branch, so `CI on main` and `CI on my-feature` are distinct lines.

### Transition logic (per cycle, per key)
Take the latest **relevant** run for the key (highest `RunNumber`):

1. **InProgress** → set `InProgress = true`, leave the settled anchor untouched, no notification.
2. **Settled**:
   - **No prior state for this key** → seed `LastSettledStatus`, **no notification** (silent seed).
   - **Prior exists, settled status changed** → fire `Success→Failure` (broke) / `Failure→Success` (recovered); update anchor + `LastRunId`.
   - **Unchanged** → update `InProgress`/`LastRunId`, no notification.

The silent-seed rule subsumes the "first poll" case — on cycle 1 every key is new, so the baseline is silent without a separate flag.

### Tray aggregation
Priority **Grey** (disconnected/error) › **Red** (any `Failure`) › **Amber** (any `InProgress`) › **Green**. Red beats Amber so a known breakage stays visible while something re-runs.

---

## 2. Relevance rules (`Rules/`)

Rules are **pure and synchronous** — field comparisons over an already-enriched `RunRecord`. A run is relevant if **any enabled** rule returns true.

```csharp
public record Identity(string Login);                 // + IReadOnlyList<string> CommitEmails with rule 4
public record RepoConfig(string Owner, string Name, string DefaultBranch);  // persisted + runtime repo type

public interface IRelevanceRule
{
    string Id { get; }       // stable key matching the settings toggle
    bool DefaultEnabled { get; }
    bool IsRelevant(RunRecord run, Identity me, RepoConfig repo);
}
```

| Rule | Id | Logic | Default |
|------|----|-------|---------|
| Triggered by me | `TriggeredByMe` | `run.TriggeringActorLogin == me.Login` | on |
| My pull request | `MyPullRequest` | `run.PullRequestAuthorLogins.Contains(me.Login)` | on |
| Default branch breaking | `DefaultBranchBreaking` | `run.HeadBranch == repo.DefaultBranch` (authorship-agnostic) | **off** |

Toggles stored per `Id` in `AppSettings` (`Dictionary<string,bool>` — extensible for rule 4 with no schema change); `RelevanceEngine.DefaultToggles()` derives the defaults from the rules. `RepoConfig.DefaultBranch` comes free from the `Repository.Get` access-validation on repo-add. (`RepoContext` from the original sketch was consolidated into the single `RepoConfig` type to avoid duplication.)

**Rule 4 (fast-follow):** adds `CommitEmails` to `Identity`, makes a Compare-API call for the run's push range, matches login **or** configured email against commit authors. The first inherently-async rule — hence a fast-follow, not MVP.

---

## 3. GitHub / ETag gateway (`GitHub/`)

```csharp
public interface IGitHubGateway
{
    Task<RepoConfig> ValidateAndDescribeAsync(string owner, string name, CancellationToken ct);
    Task<IReadOnlyList<RunRecord>> GetRecentRunsAsync(RepoConfig repo, CancellationToken ct); // one page, per_page=50
    RateLimitInfo? LastRateLimit { get; }
}
```

### ETag conditional requests
A `DelegatingHandler` (`ConditionalGetHandler`) sits below Octokit, injected via `new Connection(productHeader, new Octokit.Internal.HttpClientAdapter(() => handler))` (confirmed against Octokit 14). It keeps an in-memory map `requestUri → (etag, bufferedBody)`:

- **Outgoing**: attach `If-None-Match` if an ETag is stored for the URI.
- **200**: store ETag + buffer body, pass through.
- **304** (no body from GitHub): **synthesise a 200** from the buffered body (re-attach the stored ETag) so Octokit deserialises normally and never sees the 304.

A 304 **does not count against the 5,000/hr quota** — that's the whole point. The poll loop stays dumb: it reprocesses the (cached) runs every cycle and the idempotent state machine yields no transitions on unchanged data. The map is per-process; reset on restart feeds the silent-seed baseline.

### PR-author enrichment
The run payload's `pull_requests[]` carries **no author**, so `MyPullRequest` can't be answered from the run alone. The gateway resolves it via `PullRequest.Get`, **cached permanently by (owner, repo, PR#)** (authors are immutable) → populates `RunRecord.PullRequestAuthorLogins`. One lookup per PR ever seen; the rule stays a pure `.Contains()`.

### Rate limit & auth
- Read `GetLastApiInfo().RateLimit` each cycle; back off when `Remaining < max(100, 3×repoCount)`; honour secondary-limit `Retry-After`.
- `401`/`AuthorizationException` (revocation) → grey tray + trip the re-auth path (§5).

---

## 4. Poll loop (`Polling/`)

`PollingService` exposes `PollOnceAsync(AppSettings, ct)` (the testable cycle) and `RunAsync(ISettingsStore, ct)` (a `PeriodicTimer` loop that re-reads settings each cycle). Events: `Transition` (NotificationPayload), `Aggregate` (TrayStatus), `Snapshot` (IReadOnlyList<PipelineState>), `AuthenticationFailed`, `PollFailed`.

```
read settings fresh (repos, toggles, interval, identity)
try:
    latestPerKey = {}
    foreach repo: runs = gateway.GetRecentRunsAsync(repo)
                  relevant = runs.Where(engine.IsRelevant)        // filter BEFORE grouping
                  keep max-RunNumber per PipelineKey
    foreach (key, run): apply transition logic → maybe Transition
    prune stale keys
    raise Aggregate + Snapshot
    check LastRateLimit → back off if low
catch auth → Aggregate(Grey) + signal re-auth
catch rate → Aggregate(Grey) + pause until reset
catch     → Aggregate(Grey) + log
```

- **Relevance filtered before grouping**: relevance is per-run (spec §5), so a pipeline line is built only from relevant runs. Consequence: someone else's failing run on my branch is filtered out by `TriggeredByMe` — `DefaultBranchBreaking` is what catches breakage on `main` regardless of author. Rules compose; no special-casing.
- **Threading**: single sequential loop ⇒ single writer on the state map, no locks in Core; immutable event payloads ⇒ race-free UI reads.
- **Settings**: read at the top of each cycle (repo/toggle changes apply at the next boundary); interval change sets `PeriodicTimer.Period`.
- **Pruning**: drop keys absent from a cycle's relevant set, but **retain `Failure` keys longer** than `Success`/`Unknown` so a quiet red branch can still report its eventual recovery.

`NotificationPayload` (spec §6): repo, workflow, branch/PR, result, triggering actor, `HtmlUrl` — a thin projection of `RunRecord`.

---

## 5. Auth / device flow (`Auth/`)

**Decision (amends SPEC §3): non-expiring token, no refresh.** Refresh needs `client_secret` → would break "no embedded secret / no central server". OAuth App configured with **expiring user tokens OFF**; only recovery is re-auth on 401. `client_id` is not a secret.

```csharp
public interface ITokenStore   // platform impls in the App project
{
    Task<string?> GetTokenAsync();
    Task SaveTokenAsync(string token);
    Task ClearAsync();
}

public interface IDeviceFlowAuthenticator   // raw HttpClient — Octokit can't do device flow
{
    Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken ct);                 // POST github.com/login/device/code
    Task<string> PollForTokenAsync(DeviceCodeResponse code, CancellationToken ct);         // POST github.com/login/oauth/access_token
}
```

The device-code request takes `client_id` + scopes only. `PollForTokenAsync` owns the poll loop and an injectable delay; it handles `authorization_pending` (keep polling), `slow_down` (honour the new interval, else +5s), `expired_token`/`access_denied` (throw `DeviceFlowException`), and success (return the token). The user code is surfaced to the UI by `AuthCoordinator` (via an `onCodeReady` callback) *before* polling begins, so no intermediate `IProgress` reporter is needed.

**AuthCoordinator**: `GetOrAuthenticate` → stored token short-circuits the flow; otherwise `RequestDeviceCode` → `onCodeReady(code)` (UI shows `user_code` + opens `verification_uri`) → `PollForToken` → store token. `Reauthenticate` clears then re-runs the flow (the gateway-401 recovery path).

**Scopes**: read-only `repo` (classic) or fine-grained `contents:read, actions:read, pull_requests:read`.

---

## 6. App shell (`Heimdall` — tray, notifications, UI)

`HeimdallOrchestrator` is the runtime brain: it owns the `TrayIcon` + `NativeMenu`, runs onboarding when there's no token, builds the gateway/polling service once authenticated, and marshals every `PollingService` event to the UI thread (`Dispatcher.UIThread.Post`) — `Aggregate` → tray icon, `Snapshot` → tray menu, `Transition` → notification, `AuthenticationFailed` → re-auth.

- **Tray** (code-driven, not XAML, for control): icon per `TrayStatus` from `Assets/tray-{green,red,amber,grey}.png`; menu lists each pipeline (click → open the run), plus Settings and Quit. The app is tray-only (`ShutdownMode.OnExplicitShutdown`, no main window).
- **Notifications** (`INotificationManager`, Core interface; platform impls in the app): Linux `notify-send`, macOS `osascript`, Windows PowerShell toast, selected by `NotificationManagerFactory`. `NotificationContent.Format` (pure, tested) builds the title/body. notify-send/osascript have no activation callback, so **click-to-open a run is offered via the tray menu**; the notification body itself is informational.
- **Single instance**: `SingleInstanceLock` holds an exclusive `FileStream` (`FileShare.None`) for the process lifetime; a second launch exits quietly.
- **ViewModels** (`CommunityToolkit.Mvvm`, UI-framework-free): `DeviceFlowViewModel` (onboarding state), `SettingsViewModel` (repos/identity/toggles/interval, validates a new repo via the gateway, persists). Tested as POCOs in `Heimdall.UiTests`.
- **OAuth `client_id`** lives in `GitHubOAuth.ClientId` (public, not a secret) — a placeholder until the OAuth App is registered.

Token stores and `RepoConfig` live in Core (Avalonia-free) rather than the app, so they're testable from `Heimdall.Tests`; only the tray/notification/window glue lives in the app.

---

## Decision log

1. Pipeline line keyed **per-branch**: `(repo, workflow, branch)`.
2. **Silent seed** on first sighting of any key (subsumes silent-first-poll).
3. Transitions fire only between **settled** states; `Success↔Failure` only (`Unknown`/`InProgress` never notify).
4. Rule 2 needs a **cached PR-author lookup** — cost relocated into the gateway; rule stays pure.
5. ETag handler saves **quota only**; poll loop reprocesses idempotently (no 304 signal threaded up).
6. Tray priority **Grey › Red › Amber › Green**.
7. Pruning retains `Failure` keys longer than `Success`.
8. **Non-expiring token, no refresh** (amends SPEC §3); re-auth on 401 only.

---

## Suggested build sequence

1. **Models + state machine** (`Models/`, `Polling/` transition logic) — pure, fully unit-testable with synthetic `RunRecord`s. No GitHub yet.
2. **Relevance rules** (`Rules/`) — pure, table-driven tests.
3. **GitHub gateway** — run mapping + PR-author cache first; **ETag handler** second (the trickiest unit; test the 304-replay against a stub `HttpMessageHandler`).
4. **PollingService** — wire gateway + rules + state; test with a fake gateway driving scripted run sequences through transitions.
5. **Auth** — device-flow coordinator (raw HttpClient, testable against a stub), then platform `ITokenStore` impls.
6. **App wiring** — TrayIcon, platform `INotificationManager`, settings UI, onboarding/device-flow UI; prune Avalonia template extras; add green/red/amber/grey tray icons.

Outstanding scaffold cleanup (carried from the skeleton): prune `MainWindow`/`MainWindowViewModel`/`ViewLocator`, replace the placeholder `avalonia-logo.ico` with the four tray icons.

---

## Testing strategy

**Rule of thumb: if it's worth testing, it lives in Core or a ViewModel — never in a view.** This keeps pressure on the architecture to push logic out of the UI.

### For now (build-sequence steps 1–4) — Core unit tests only
Everything in this stretch is pure logic. Keep `Heimdall.Tests` Avalonia-free (xunit 2.9.3 + Shouldly, references Core only). **Do not stand up a UI test project yet** — there are no views to assert on.

- **State machine** — table-driven over the transition cases: silent-seed on first sighting; `Success→Failure` fires; `Failure→Success` fires; `Unknown`/`InProgress` never fire; in-progress holds the settled anchor. Synthetic `RunRecord`s, no I/O.
- **Rules** — table-driven `IsRelevant` per rule + OR composition + toggle on/off.
- **Gateway** — the fiddly unit is the **ETag handler**: test the 304-replay against a stub `HttpMessageHandler` (200 stores+buffers; 304 → synthesised 200 with the buffered body). Plus the PR-author cache (one lookup per PR, cache hit thereafter).
- **PollingService** — drive a **fake `IGitHubGateway`** through scripted run sequences; assert emitted `Transition`/`Aggregate`/`Snapshot` events.

### Later (step 6, app wiring)
- **ViewModel POCO tests first** — VMs are plain `CommunityToolkit.Mvvm` objects (settings logic, device-flow VM state `pending → code shown → authorised`, command enablement). No headless needed.
- **`Avalonia.Headless.XUnit` (`[AvaloniaFact]`) — sparingly**, only for genuine view concerns a VM can't express (bindings resolve, device-flow view surfaces the `user_code`, command wiring). Needs a **separate** `Heimdall.UiTests` project (references the app, `[assembly: AvaloniaTestApplication(...)]` + headless `AppBuilder`) — match the package to Avalonia 12.0.4. Defer until a view actually warrants it.
- **Platform seams not unit-testable** — `TrayIcon`, `INotificationManager`, `ITokenStore` are platform-native. Verify via manual per-platform smoke tests or a small console harness. **Appium end-to-end: skip for MVP.**
