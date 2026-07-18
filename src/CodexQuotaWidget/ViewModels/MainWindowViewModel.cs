using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexQuotaWidget.Models;
using CodexQuotaWidget.Services;

namespace CodexQuotaWidget.ViewModels;

public enum QuotaStatusTone
{
    Current,
    Stale,
    Critical,
    Unavailable
}

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly Func<DateTimeOffset> _clock;
    private QuotaSnapshot? _snapshot;
    private string _serviceStatus = "等待 Codex 产生用量信息";
    private CodexProcessState _processState = CodexProcessState.Unknown;

    public MainWindowViewModel(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<QuotaWindowViewModel> Windows { get; } = [];

    public bool HasSnapshot => _snapshot is not null;

    public bool IsStale =>
        _snapshot is not null && _clock() - _snapshot.CapturedAt >= TimeSpan.FromMinutes(15);

    public string PlanText => string.IsNullOrWhiteSpace(_snapshot?.PlanType)
        ? "本地额度快照"
        : $"{_snapshot.PlanType.ToUpperInvariant()} · 本地快照";

    public string SnapshotAgeText => _snapshot is null
        ? "尚无快照"
        : FormatAge(_clock() - _snapshot.CapturedAt);

    public QuotaStatusTone StatusTone
    {
        get
        {
            if (_snapshot is null)
            {
                return QuotaStatusTone.Unavailable;
            }

            if (IsStale)
            {
                return QuotaStatusTone.Stale;
            }

            return Windows.Any(window => window.IsCritical)
                ? QuotaStatusTone.Critical
                : QuotaStatusTone.Current;
        }
    }

    public string StatusText
    {
        get
        {
            if (_snapshot is null)
            {
                return _serviceStatus;
            }

            if (IsStale)
            {
                return $"快照可能已过时 · {SnapshotAgeText}";
            }

            if (Windows.Any(window => window.IsCritical))
            {
                return $"额度较低 · {SnapshotAgeText}";
            }

            var processText = _processState switch
            {
                CodexProcessState.Running => "Codex 运行中",
                CodexProcessState.NotRunning => "Codex 未运行",
                _ => "Codex 状态未知"
            };
            return $"{processText} · {SnapshotAgeText}";
        }
    }

    public void ApplySnapshot(QuotaSnapshot snapshot)
    {
        _snapshot = snapshot;
        Windows.Clear();
        if (snapshot.Primary is not null)
        {
            Windows.Add(new QuotaWindowViewModel(snapshot.Primary, _clock));
        }

        if (snapshot.Secondary is not null)
        {
            Windows.Add(new QuotaWindowViewModel(snapshot.Secondary, _clock));
        }

        NotifySnapshotProperties();
    }

    public void SetServiceStatus(string status)
    {
        _serviceStatus = status;
        if (_snapshot is null)
        {
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public void SetProcessState(CodexProcessState state)
    {
        _processState = state;
        OnPropertyChanged(nameof(StatusText));
    }

    public void Refresh()
    {
        foreach (var window in Windows)
        {
            window.Refresh();
        }

        NotifySnapshotProperties();
    }

    private void NotifySnapshotProperties()
    {
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(PlanText));
        OnPropertyChanged(nameof(SnapshotAgeText));
        OnPropertyChanged(nameof(StatusTone));
        OnPropertyChanged(nameof(StatusText));
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero || age < TimeSpan.FromMinutes(1))
        {
            return "刚刚更新";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes} 分钟前";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{(int)age.TotalHours} 小时前";
        }

        return $"{(int)age.TotalDays} 天前";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class QuotaWindowViewModel : INotifyPropertyChanged
{
    private readonly Func<DateTimeOffset> _clock;

    public QuotaWindowViewModel(QuotaWindow window, Func<DateTimeOffset> clock)
    {
        _clock = clock;
        RemainingPercent = window.RemainingPercent;
        WindowMinutes = window.WindowMinutes;
        ResetsAtLocal = window.ResetsAt.ToLocalTime();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double RemainingPercent { get; }

    public int WindowMinutes { get; }

    public DateTimeOffset ResetsAtLocal { get; }

    public bool IsCritical => RemainingPercent < 20d;

    public string Label => FormatWindowLabel(WindowMinutes);

    public string RemainingText => $"{RemainingPercent:0.#}%";

    public string ResetText
    {
        get
        {
            var remaining = ResetsAtLocal - _clock().ToLocalTime();
            var countdown = remaining <= TimeSpan.Zero
                ? "等待新快照"
                : remaining.TotalDays >= 1
                    ? $"约 {(int)Math.Ceiling(remaining.TotalDays)} 天"
                    : remaining.TotalHours >= 1
                        ? $"约 {(int)Math.Ceiling(remaining.TotalHours)} 小时"
                        : $"约 {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} 分钟";
            return $"{ResetsAtLocal:MM-dd HH:mm} 重置 · {countdown}";
        }
    }

    public static string FormatWindowLabel(int minutes) => minutes switch
    {
        300 => "5 小时",
        10080 => "每周",
        _ when minutes > 0 && minutes % 1440 == 0 => $"{minutes / 1440} 天",
        _ when minutes > 0 && minutes % 60 == 0 => $"{minutes / 60} 小时",
        _ => $"{minutes} 分钟"
    };

    public void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResetText)));
}
