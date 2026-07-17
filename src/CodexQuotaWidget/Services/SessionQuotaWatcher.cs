using System.IO;
using CodexQuotaWidget.Models;

namespace CodexQuotaWidget.Services;

public sealed class SessionQuotaWatcher : IDisposable
{
    private readonly string _sessionsRoot;
    private readonly RolloutQuotaReader _reader;
    private readonly object _sync = new();
    private readonly Dictionary<string, Timer> _debounceTimers =
        new(StringComparer.OrdinalIgnoreCase);
    private QuotaSnapshot? _currentSnapshot;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public SessionQuotaWatcher(string sessionsRoot, RolloutQuotaReader reader)
    {
        _sessionsRoot = sessionsRoot;
        _reader = reader;
    }

    public event EventHandler<QuotaSnapshot>? SnapshotChanged;

    public event EventHandler<string>? StatusChanged;

    public QuotaSnapshot? CurrentSnapshot
    {
        get
        {
            lock (_sync)
            {
                return _currentSnapshot;
            }
        }
    }

    public QuotaSnapshot? ScanNow()
    {
        ThrowIfDisposed();

        if (!Directory.Exists(_sessionsRoot))
        {
            StatusChanged?.Invoke(this, "未找到 Codex 会话目录");
            return CurrentSnapshot;
        }

        QuotaSnapshot? newest = null;
        foreach (var day in new[] { DateTime.Today, DateTime.Today.AddDays(-1) })
        {
            var dayPath = Path.Combine(
                _sessionsRoot,
                day.ToString("yyyy"),
                day.ToString("MM"),
                day.ToString("dd"));

            foreach (var file in EnumerateRecentRollouts(dayPath))
            {
                var snapshot = _reader.ReadLatest(file);
                if (snapshot is not null &&
                    (newest is null || snapshot.CapturedAt > newest.CapturedAt))
                {
                    newest = snapshot;
                }
            }
        }

        if (newest is null)
        {
            StatusChanged?.Invoke(this, "等待 Codex 产生用量信息");
        }
        else
        {
            PublishIfNewer(newest);
        }

        return CurrentSnapshot;
    }

    public void Start()
    {
        ThrowIfDisposed();

        if (!Directory.Exists(_sessionsRoot))
        {
            _ = ScanNow();
            return;
        }

        lock (_sync)
        {
            if (_watcher is not null)
            {
                return;
            }

            _watcher = new FileSystemWatcher(_sessionsRoot, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName |
                    NotifyFilters.CreationTime |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnRolloutChanged;
            _watcher.Created += OnRolloutChanged;
            _watcher.Renamed += OnRolloutRenamed;
        }

        _ = ScanNow();
    }

    public void Dispose()
    {
        FileSystemWatcher? watcher;
        Timer[] timers;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            watcher = _watcher;
            _watcher = null;
            timers = _debounceTimers.Values.ToArray();
            _debounceTimers.Clear();
        }

        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnRolloutChanged;
            watcher.Created -= OnRolloutChanged;
            watcher.Renamed -= OnRolloutRenamed;
            watcher.Dispose();
        }

        foreach (var timer in timers)
        {
            timer.Dispose();
        }
    }

    private static IEnumerable<string> EnumerateRecentRollouts(string dayPath)
    {
        if (!Directory.Exists(dayPath))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(dayPath, "*.jsonl", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Take(20)
                .Select(info => info.FullName)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void PublishIfNewer(QuotaSnapshot snapshot)
    {
        var changed = false;
        lock (_sync)
        {
            if (!_disposed &&
                (_currentSnapshot is null || snapshot.CapturedAt > _currentSnapshot.CapturedAt))
            {
                _currentSnapshot = snapshot;
                changed = true;
            }
        }

        if (changed)
        {
            SnapshotChanged?.Invoke(this, snapshot);
            StatusChanged?.Invoke(this, "额度快照已更新");
        }
    }

    private void OnRolloutChanged(object sender, FileSystemEventArgs eventArgs) =>
        Debounce(eventArgs.FullPath);

    private void OnRolloutRenamed(object sender, RenamedEventArgs eventArgs) =>
        Debounce(eventArgs.FullPath);

    private void Debounce(string path)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_debounceTimers.Remove(path, out var previous))
            {
                previous.Dispose();
            }

            _debounceTimers[path] = new Timer(
                _ => ProcessChangedPath(path),
                state: null,
                dueTime: TimeSpan.FromMilliseconds(300),
                period: Timeout.InfiniteTimeSpan);
        }
    }

    private void ProcessChangedPath(string path)
    {
        Timer? timer;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _debounceTimers.Remove(path, out timer);
        }

        timer?.Dispose();
        var snapshot = _reader.ReadLatest(path);
        if (snapshot is not null)
        {
            PublishIfNewer(snapshot);
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
