using System.Text.Json;
using CodexQuotaWidget.Services;

namespace CodexQuotaWidget.Tests;

public sealed class SessionQuotaWatcherTests
{
    [Fact]
    public void ScanNow_SelectsNewestSnapshotAcrossTodayAndYesterday()
    {
        using var sessions = new TemporaryDirectory();
        var yesterday = sessions.CreateDay(DateTime.Today.AddDays(-1));
        var today = sessions.CreateDay(DateTime.Today);
        var newestTimestamp = DateTimeOffset.Now.AddMinutes(-1);

        File.WriteAllText(
            Path.Combine(today, "rollout-current.jsonl"),
            CreateEvent(newestTimestamp.AddMinutes(-10), 80) + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(yesterday, "rollout-yesterday.jsonl"),
            CreateEvent(newestTimestamp, 20) + Environment.NewLine);

        using var watcher = new SessionQuotaWatcher(sessions.Path, new RolloutQuotaReader());

        var snapshot = watcher.ScanNow();

        Assert.Equal(newestTimestamp, snapshot!.CapturedAt);
        Assert.Equal(80d, snapshot.Primary!.RemainingPercent);
    }

    [Fact]
    public void ScanNow_ReportsUnavailableWhenSessionsDirectoryIsMissing()
    {
        using var parent = new TemporaryDirectory();
        var status = string.Empty;
        using var watcher = new SessionQuotaWatcher(
            System.IO.Path.Combine(parent.Path, "missing"),
            new RolloutQuotaReader());
        watcher.StatusChanged += (_, message) => status = message;

        var snapshot = watcher.ScanNow();

        Assert.Null(snapshot);
        Assert.Contains("未找到", status);
    }

    [Fact]
    public async Task Start_PublishesNewSnapshotAfterFileAppend()
    {
        using var sessions = new TemporaryDirectory();
        var today = sessions.CreateDay(DateTime.Today);
        var path = System.IO.Path.Combine(today, "rollout-live.jsonl");
        var initial = DateTimeOffset.Now.AddMinutes(-2);
        var appended = initial.AddMinutes(1);
        File.WriteAllText(path, CreateEvent(initial, 80) + Environment.NewLine);

        using var watcher = new SessionQuotaWatcher(sessions.Path, new RolloutQuotaReader());
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.CapturedAt == appended)
            {
                received.TrySetResult();
            }
        };

        watcher.Start();
        File.AppendAllText(path, CreateEvent(appended, 30) + Environment.NewLine);

        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(appended, watcher.CurrentSnapshot!.CapturedAt);
        Assert.Equal(70d, watcher.CurrentSnapshot.Primary!.RemainingPercent);
    }

    [Fact]
    public async Task Start_DoesNotReplaceCurrentSnapshotWithOlderEvent()
    {
        using var sessions = new TemporaryDirectory();
        var today = sessions.CreateDay(DateTime.Today);
        var current = DateTimeOffset.Now.AddMinutes(-1);
        File.WriteAllText(
            System.IO.Path.Combine(today, "rollout-current.jsonl"),
            CreateEvent(current, 10) + Environment.NewLine);
        using var watcher = new SessionQuotaWatcher(sessions.Path, new RolloutQuotaReader());
        watcher.Start();

        var changes = 0;
        watcher.SnapshotChanged += (_, _) => Interlocked.Increment(ref changes);
        File.WriteAllText(
            System.IO.Path.Combine(today, "rollout-older.jsonl"),
            CreateEvent(current.AddMinutes(-5), 99) + Environment.NewLine);

        await Task.Delay(800);
        Assert.Equal(current, watcher.CurrentSnapshot!.CapturedAt);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Dispose_StopsCallbacksAndReleasesWatchedDirectory()
    {
        using var sessions = new TemporaryDirectory();
        var today = sessions.CreateDay(DateTime.Today);
        var path = System.IO.Path.Combine(today, "rollout-live.jsonl");
        var initial = DateTimeOffset.Now.AddMinutes(-2);
        File.WriteAllText(path, CreateEvent(initial, 50) + Environment.NewLine);
        var watcher = new SessionQuotaWatcher(sessions.Path, new RolloutQuotaReader());
        watcher.Start();

        var changes = 0;
        watcher.SnapshotChanged += (_, _) => Interlocked.Increment(ref changes);
        watcher.Dispose();
        File.AppendAllText(
            path,
            CreateEvent(initial.AddMinutes(1), 20) + Environment.NewLine);

        await Task.Delay(800);
        Assert.Equal(0, changes);

        // TemporaryDirectory.Dispose deletes this tree, which also proves no watcher handle remains.
    }

    private static string CreateEvent(DateTimeOffset timestamp, double usedPercent) =>
        JsonSerializer.Serialize(new
        {
            timestamp,
            type = "event_msg",
            payload = new
            {
                type = "token_count",
                rate_limits = new
                {
                    primary = new
                    {
                        used_percent = usedPercent,
                        window_minutes = 300,
                        resets_at = timestamp.AddHours(1).ToUnixTimeSeconds()
                    }
                }
            }
        });

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"codex-quota-watcher-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDay(DateTime day)
        {
            var path = System.IO.Path.Combine(
                Path,
                day.ToString("yyyy"),
                day.ToString("MM"),
                day.ToString("dd"));
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
