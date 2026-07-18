using CodexQuotaWidget.Models;
using CodexQuotaWidget.ViewModels;

namespace CodexQuotaWidget.Tests;

public sealed class MainWindowViewModelTests
{
    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    public void ApplySnapshot_MarksSnapshotStaleAtFifteenMinutes(int ageMinutes, bool expectedStale)
    {
        var now = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.FromHours(8));
        var viewModel = new MainWindowViewModel(() => now);

        viewModel.ApplySnapshot(CreateSnapshot(now.AddMinutes(-ageMinutes), 40));

        Assert.Equal(expectedStale, viewModel.IsStale);
    }

    [Fact]
    public void NewViewModel_ShowsWaitingStateWithoutSnapshot()
    {
        var viewModel = new MainWindowViewModel();

        Assert.False(viewModel.HasSnapshot);
        Assert.Equal("等待 Codex 产生用量信息", viewModel.StatusText);
        Assert.Equal(QuotaStatusTone.Unavailable, viewModel.StatusTone);
    }

    [Fact]
    public void ApplySnapshot_UsesCriticalStateBelowTwentyPercentRemaining()
    {
        var now = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.FromHours(8));
        var viewModel = new MainWindowViewModel(() => now);

        viewModel.ApplySnapshot(CreateSnapshot(now, 81));

        Assert.True(viewModel.Windows.Single().IsCritical);
        Assert.Equal(QuotaStatusTone.Critical, viewModel.StatusTone);
    }

    [Fact]
    public void ApplySnapshot_ExposesOnlyWindowsThatArePresent()
    {
        var now = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.FromHours(8));
        var viewModel = new MainWindowViewModel(() => now);
        var snapshot = new QuotaSnapshot(
            now,
            "plus",
            "codex",
            null,
            new QuotaWindow(25, 10080, now.AddDays(3)),
            null);

        viewModel.ApplySnapshot(snapshot);

        var window = Assert.Single(viewModel.Windows);
        Assert.Equal("每周", window.Label);
    }

    [Fact]
    public void ApplySnapshot_ConvertsResetTimestampToLocalTime()
    {
        var now = new DateTimeOffset(2026, 7, 17, 2, 0, 0, TimeSpan.Zero);
        var reset = now.AddHours(3);
        var viewModel = new MainWindowViewModel(() => now);
        var snapshot = new QuotaSnapshot(
            now,
            "plus",
            "codex",
            new QuotaWindow(30, 300, reset),
            null,
            null);

        viewModel.ApplySnapshot(snapshot);

        Assert.Equal(reset.ToLocalTime(), viewModel.Windows.Single().ResetsAtLocal);
    }

    private static QuotaSnapshot CreateSnapshot(DateTimeOffset capturedAt, double usedPercent) =>
        new(
            capturedAt,
            "plus",
            "codex",
            new QuotaWindow(usedPercent, 300, capturedAt.AddHours(1)),
            null,
            null);
}
