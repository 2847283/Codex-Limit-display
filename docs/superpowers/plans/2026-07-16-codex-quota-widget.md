# Codex Quota Widget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a lightweight Windows desktop widget that reads Codex's latest local rate-limit snapshot while tasks run and displays remaining quota, reset time, and snapshot freshness.

**Architecture:** A .NET 9 WPF application watches `%USERPROFILE%\.codex\sessions` for appended JSONL rollout files. It accepts only `event_msg` records whose payload type is `token_count`, maps `payload.rate_limits` into a small immutable model, and renders the newest snapshot without reading credentials or calling network APIs. The window is attached to the Windows desktop host when possible and falls back to a normal non-topmost borderless window when Explorer integration is unavailable.

**Tech Stack:** C# 13, .NET 9, WPF, System.Text.Json, FileSystemWatcher, Win32 P/Invoke, xUnit.

---

## Product rules

- Launch manually; do not create a startup entry, service, scheduled task, or tray process.
- Publish one self-contained Windows x64 executable.
- Read only `%USERPROFILE%\.codex\sessions\**\*.jsonl`.
- Never read `auth.json`, browser state, cookies, account tokens, or task message text.
- Never make HTTP requests and never start or bundle a Codex CLI process.
- Treat snapshots as exact at their capture time, not as live server state.
- Mark snapshots older than 15 minutes as possibly stale.
- Compute remaining percentage as `Math.Clamp(100 - usedPercent, 0, 100)`.
- Render whichever quota windows are present. Either `primary` or `secondary` may be null.
- Do not assume that primary means five hours or that secondary means weekly; derive the label from `window_minutes`.
- Do not show a task title, account email, prompt, response, or historical conversation content.

## Target file map

```text
CodexQuotaWidget.sln
src/CodexQuotaWidget/
├─ CodexQuotaWidget.csproj
├─ App.xaml
├─ App.xaml.cs
├─ MainWindow.xaml
├─ MainWindow.xaml.cs
├─ Models/QuotaSnapshot.cs
├─ Services/RolloutQuotaReader.cs
├─ Services/SessionQuotaWatcher.cs
├─ Services/CodexProcessDetector.cs
├─ Services/WidgetSettingsStore.cs
├─ ViewModels/MainWindowViewModel.cs
└─ Interop/DesktopHost.cs
tests/CodexQuotaWidget.Tests/
├─ CodexQuotaWidget.Tests.csproj
├─ RolloutQuotaReaderTests.cs
├─ SessionQuotaWatcherTests.cs
├─ MainWindowViewModelTests.cs
└─ WidgetSettingsStoreTests.cs
```

## Data contracts

Create `src/CodexQuotaWidget/Models/QuotaSnapshot.cs` with these exact public records:

```csharp
namespace CodexQuotaWidget.Models;

public sealed record QuotaWindow(
    double UsedPercent,
    int WindowMinutes,
    DateTimeOffset ResetsAt)
{
    public double RemainingPercent =>
        Math.Clamp(100d - UsedPercent, 0d, 100d);
}

public sealed record CreditSnapshot(
    bool HasCredits,
    bool Unlimited,
    string? Balance);

public sealed record QuotaSnapshot(
    DateTimeOffset CapturedAt,
    string? PlanType,
    string? LimitId,
    QuotaWindow? Primary,
    QuotaWindow? Secondary,
    CreditSnapshot? Credits);
```

Window labels must use this deterministic mapping:

```csharp
public static string FormatWindowLabel(int minutes) => minutes switch
{
    300 => "5 小时",
    10080 => "每周",
    _ when minutes > 0 && minutes % 1440 == 0 => $"{minutes / 1440} 天",
    _ when minutes > 0 && minutes % 60 == 0 => $"{minutes / 60} 小时",
    _ => $"{minutes} 分钟"
};
```

### Task 1: Scaffold the solution

**Files:**
- Create: `CodexQuotaWidget.sln`
- Create: `src/CodexQuotaWidget/CodexQuotaWidget.csproj`
- Create: `tests/CodexQuotaWidget.Tests/CodexQuotaWidget.Tests.csproj`

- [ ] **Step 1: Create the WPF and test projects**

Run:

```powershell
dotnet new sln -n CodexQuotaWidget
dotnet new wpf -n CodexQuotaWidget -o src/CodexQuotaWidget -f net9.0
dotnet new xunit -n CodexQuotaWidget.Tests -o tests/CodexQuotaWidget.Tests -f net9.0
dotnet sln add src/CodexQuotaWidget/CodexQuotaWidget.csproj
dotnet sln add tests/CodexQuotaWidget.Tests/CodexQuotaWidget.Tests.csproj
dotnet add tests/CodexQuotaWidget.Tests/CodexQuotaWidget.Tests.csproj reference src/CodexQuotaWidget/CodexQuotaWidget.csproj
```

Expected: the solution contains one WPF project and one xUnit project.

- [ ] **Step 2: Run the template tests**

Run:

```powershell
dotnet test CodexQuotaWidget.sln
```

Expected: build succeeds and the template test passes.

- [ ] **Step 3: Commit the scaffold**

```powershell
git add CodexQuotaWidget.sln src tests
git commit -m "chore: scaffold quota widget"
```

### Task 2: Parse rate-limit snapshots from rollout tails

**Files:**
- Create: `src/CodexQuotaWidget/Models/QuotaSnapshot.cs`
- Create: `src/CodexQuotaWidget/Services/RolloutQuotaReader.cs`
- Create: `tests/CodexQuotaWidget.Tests/RolloutQuotaReaderTests.cs`
- Delete: `tests/CodexQuotaWidget.Tests/UnitTest1.cs`

- [ ] **Step 1: Write parser tests using temporary JSONL files**

Each test fixture must contain complete JSONL lines. Use this canonical valid event:

```json
{"timestamp":"2026-07-16T12:58:43.758Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","plan_type":"plus","primary":{"used_percent":39.0,"window_minutes":10080,"resets_at":1784797605},"secondary":null,"credits":{"has_credits":false,"unlimited":false,"balance":"0"}}}}
```

Write tests proving that:

```csharp
Assert.Equal(61d, snapshot.Primary!.RemainingPercent);
Assert.Equal(10080, snapshot.Primary.WindowMinutes);
Assert.Null(snapshot.Secondary);
Assert.Equal("plus", snapshot.PlanType);
Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784797605), snapshot.Primary.ResetsAt);
```

Also add separate tests for:

- primary and secondary both present;
- a partial JSON line at end-of-file;
- a user-message line containing the text `rate_limits`;
- several token-count events where the newest timestamp wins;
- used percentages below zero and above 100, verifying the computed remainder is clamped.

- [ ] **Step 2: Run the parser tests and confirm failure**

Run:

```powershell
dotnet test CodexQuotaWidget.sln --filter RolloutQuotaReaderTests
```

Expected: compilation fails because `RolloutQuotaReader` and the model records do not exist.

- [ ] **Step 3: Implement tail-only JSONL parsing**

Implement this exact public entry point:

```csharp
public sealed class RolloutQuotaReader
{
    public const int MaximumTailBytes = 1_048_576;

    public QuotaSnapshot? ReadLatest(string rolloutPath);
}
```

Implementation requirements:

1. Open with `FileAccess.Read` and `FileShare.ReadWrite | FileShare.Delete`.
2. Seek to `Math.Max(0, stream.Length - MaximumTailBytes)`.
3. When the seek position is nonzero, discard bytes through the first newline.
4. Decode complete UTF-8 lines and inspect them from newest to oldest.
5. Accept only top-level `type == "event_msg"` with `payload.type == "token_count"`.
6. Read only `timestamp` and `payload.rate_limits` fields with `JsonDocument`.
7. Ignore malformed lines, missing windows, invalid timestamps, and partially written trailing lines.
8. Never log the input line or serialize the complete event.

- [ ] **Step 4: Run all tests**

```powershell
dotnet test CodexQuotaWidget.sln
```

Expected: all parser tests pass.

- [ ] **Step 5: Commit the parser**

```powershell
git add src/CodexQuotaWidget/Models src/CodexQuotaWidget/Services/RolloutQuotaReader.cs tests/CodexQuotaWidget.Tests
git commit -m "feat: parse Codex quota snapshots"
```

### Task 3: Discover and watch active session files

**Files:**
- Create: `src/CodexQuotaWidget/Services/SessionQuotaWatcher.cs`
- Create: `src/CodexQuotaWidget/Services/CodexProcessDetector.cs`
- Create: `tests/CodexQuotaWidget.Tests/SessionQuotaWatcherTests.cs`

- [ ] **Step 1: Write watcher tests**

Use a temporary directory shaped like:

```text
sessions/2026/07/16/rollout-current.jsonl
sessions/2026/07/15/rollout-yesterday.jsonl
```

Tests must prove:

- initial scan chooses the latest captured snapshot rather than the newest filename;
- today and yesterday are both scanned;
- a newly appended valid event replaces the previous snapshot;
- an older event cannot replace a newer snapshot;
- a missing sessions directory reports an unavailable state without throwing;
- disposal stops callbacks and releases directory handles.

- [ ] **Step 2: Run watcher tests and confirm failure**

```powershell
dotnet test CodexQuotaWidget.sln --filter SessionQuotaWatcherTests
```

Expected: compilation fails because `SessionQuotaWatcher` does not exist.

- [ ] **Step 3: Implement startup scanning and debounced watching**

Expose:

```csharp
public sealed class SessionQuotaWatcher : IDisposable
{
    public event EventHandler<QuotaSnapshot>? SnapshotChanged;
    public event EventHandler<string>? StatusChanged;

    public SessionQuotaWatcher(string sessionsRoot, RolloutQuotaReader reader);
    public QuotaSnapshot? ScanNow();
    public void Start();
    public void Dispose();
}
```

Behavior:

- default root in the application is `Path.Combine(UserProfile, ".codex", "sessions")`;
- scan the current and previous local calendar day;
- inspect no more than the 20 most recently modified `.jsonl` files per day;
- configure `FileSystemWatcher` for `*.jsonl`, including subdirectories and filename, creation, write, and size notifications;
- debounce each changed path for 300 milliseconds;
- parse only the changed file after startup;
- publish only snapshots newer than the current snapshot;
- marshal UI updates through the WPF dispatcher in the caller, not inside the service.

`CodexProcessDetector` must report running when a process named `codex` exists or a `ChatGPT` process path contains `OpenAI.Codex`. Access-denied exceptions must return an unknown state rather than terminate the application. This detector is status decoration only and must not gate file watching.

- [ ] **Step 4: Run the tests**

```powershell
dotnet test CodexQuotaWidget.sln
```

Expected: all scanner and watcher tests pass without reading the real user profile.

- [ ] **Step 5: Commit the watcher**

```powershell
git add src/CodexQuotaWidget/Services tests/CodexQuotaWidget.Tests/SessionQuotaWatcherTests.cs
git commit -m "feat: watch active Codex sessions"
```

### Task 4: Present quota state in a WPF card

**Files:**
- Create: `src/CodexQuotaWidget/ViewModels/MainWindowViewModel.cs`
- Modify: `src/CodexQuotaWidget/MainWindow.xaml`
- Modify: `src/CodexQuotaWidget/MainWindow.xaml.cs`
- Modify: `src/CodexQuotaWidget/App.xaml.cs`
- Create: `tests/CodexQuotaWidget.Tests/MainWindowViewModelTests.cs`

- [ ] **Step 1: Write view-model tests with an injectable clock**

Tests must prove:

- a snapshot captured 14 minutes ago is current;
- a snapshot captured 15 minutes ago is stale;
- no snapshot produces `等待 Codex 产生用量信息`;
- remaining values below 20 use the critical color state;
- only non-null windows are exposed to the view;
- reset timestamps are converted to local time.

- [ ] **Step 2: Implement the view model**

Use `INotifyPropertyChanged` without an external MVVM package. Store `Func<DateTimeOffset>` as the clock and expose an `ObservableCollection` containing one item per non-null quota window. Refresh relative-time and reset-countdown text once per minute with `DispatcherTimer`.

- [ ] **Step 3: Build the card layout**

Set these window properties:

```xml
Width="320"
SizeToContent="Height"
WindowStyle="None"
AllowsTransparency="True"
Background="Transparent"
ShowInTaskbar="False"
ResizeMode="NoResize"
Topmost="False"
```

Use a `#E61A1A1A` card background, 14-DIP corner radius, green current status, gray stale status, and red status below 20 percent remaining. Render each quota window with a label, remaining percentage, remaining progress bar, reset time, and the snapshot age.

The context menu must contain only:

```text
立即扫描
退出
```

The first action calls `ScanNow`; the second disposes application services and shuts down. Left-button drag calls `DragMove()`.

- [ ] **Step 4: Run tests and launch a smoke instance**

```powershell
dotnet test CodexQuotaWidget.sln
dotnet run --project src/CodexQuotaWidget/CodexQuotaWidget.csproj
```

Expected: tests pass; the card opens without a taskbar button and either shows the latest local snapshot or the waiting state.

- [ ] **Step 5: Commit the UI**

```powershell
git add src/CodexQuotaWidget tests/CodexQuotaWidget.Tests/MainWindowViewModelTests.cs
git commit -m "feat: display quota desktop card"
```

### Task 5: Attach to the desktop and persist position

**Files:**
- Create: `src/CodexQuotaWidget/Interop/DesktopHost.cs`
- Create: `src/CodexQuotaWidget/Services/WidgetSettingsStore.cs`
- Modify: `src/CodexQuotaWidget/MainWindow.xaml.cs`
- Create: `tests/CodexQuotaWidget.Tests/WidgetSettingsStoreTests.cs`

- [ ] **Step 1: Write settings-store tests**

Tests must cover valid position round-trip, missing settings, malformed JSON, and clamping a saved position into the current virtual-screen bounds.

- [ ] **Step 2: Implement settings persistence**

Store only `Left` and `Top` in:

```text
%LocalAppData%\CodexQuotaWidget\settings.json
```

Use an atomic temporary-file replacement. If loading fails, place the window 24 DIP from the top-right of the primary work area. Save after drag completes and during normal shutdown.

- [ ] **Step 3: Implement desktop hosting**

After `SourceInitialized`:

1. locate `Progman`;
2. send message `0x052C` with `SendMessageTimeout`;
3. enumerate top-level windows to find the host containing `SHELLDLL_DefView`;
4. attach the WPF HWND to that desktop host with `SetParent`;
5. verify the parent handle every five seconds and reattach after Explorer restarts.

If discovery or attachment fails, leave the card as a normal non-topmost borderless window. Never elevate privileges for desktop hosting.

- [ ] **Step 4: Verify desktop behavior**

Manual checks:

- the card is visible on the desktop;
- normal application windows cover it;
- dragging works;
- restarting Explorer does not permanently lose the card;
- restarting the widget restores a visible saved position.

- [ ] **Step 5: Commit desktop integration**

```powershell
git add src/CodexQuotaWidget/Interop src/CodexQuotaWidget/Services/WidgetSettingsStore.cs src/CodexQuotaWidget/MainWindow.xaml.cs tests/CodexQuotaWidget.Tests/WidgetSettingsStoreTests.cs
git commit -m "feat: integrate with Windows desktop"
```

### Task 6: Publish and perform acceptance checks

**Files:**
- Modify: `src/CodexQuotaWidget/CodexQuotaWidget.csproj`
- Modify: `README.md`

- [ ] **Step 1: Configure self-contained publishing**

Add these properties:

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<DebugType>none</DebugType>
<DebugSymbols>false</DebugSymbols>
```

- [ ] **Step 2: Run release verification**

```powershell
dotnet test CodexQuotaWidget.sln -c Release
dotnet publish src/CodexQuotaWidget/CodexQuotaWidget.csproj -c Release -r win-x64
```

Expected: tests pass and the publish directory contains the executable without a separate Codex CLI binary.

- [ ] **Step 3: Perform acceptance checks**

Verify all of the following:

- starting before Codex shows the waiting state;
- running a Codex task updates the card after the next `token_count` event;
- a fixture with `used_percent = 39` renders 61 percent remaining;
- a single weekly window renders one row and no empty second row;
- stale data is clearly marked after 15 minutes;
- exiting leaves no widget-owned process running;
- source search finds no `auth.json`, `HttpClient`, token extraction, or Codex CLI launch logic;
- idle CPU remains below one percent and the watcher does not repeatedly scan historical sessions.

- [ ] **Step 4: Update README status and commit**

Change the README status from planning to implemented only after the release checks pass, then commit:

```powershell
git add src README.md
git commit -m "build: publish portable quota widget"
```

## Completion criteria

The implementation is complete only when all automated tests pass, the widget updates from a real running Codex task, desktop interaction succeeds on the target Windows machine, no credential or network access exists, and the published package contains no extra Codex executable.
