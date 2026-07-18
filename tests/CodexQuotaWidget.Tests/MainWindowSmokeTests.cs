using System.Threading;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexQuotaWidget.Interop;
using CodexQuotaWidget.Models;
using CodexQuotaWidget.Services;
using CodexQuotaWidget.ViewModels;

namespace CodexQuotaWidget.Tests;

public sealed class MainWindowSmokeTests
{
    [Fact]
    public void Window_CanScanSavePositionAndExitFromContextMenu()
    {
        Exception? failure = null;
        var completed = new ManualResetEventSlim();
        var settingsDirectory = Path.Combine(
            Path.GetTempPath(),
            $"codex-quota-window-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");

        var thread = new Thread(() =>
        {
            Application? application = null;
            try
            {
                application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var viewModel = new MainWindowViewModel();
                var sessionsRoot = Path.Combine(settingsDirectory, "sessions");
                using var watcher = new SessionQuotaWatcher(
                    sessionsRoot,
                    new RolloutQuotaReader());
                using var initialScanFinished = new ManualResetEventSlim();
                watcher.StatusChanged += (_, _) => initialScanFinished.Set();
                var window = new MainWindow(
                    watcher,
                    viewModel,
                    new CodexProcessDetector(),
                    new WidgetSettingsStore(settingsPath),
                    new WindowedDesktopHost());

                Assert.True(window.AllowsTransparency);
                Assert.False(window.ShowActivated);

                window.Show();
                window.Dispatcher.Invoke(window.UpdateLayout, DispatcherPriority.Loaded);
                Assert.True(initialScanFinished.Wait(TimeSpan.FromSeconds(3)));

                var capturedAt = DateTimeOffset.Now;
                var dayDirectory = Path.Combine(
                    sessionsRoot,
                    capturedAt.ToString("yyyy"),
                    capturedAt.ToString("MM"),
                    capturedAt.ToString("dd"));
                Directory.CreateDirectory(dayDirectory);
                File.WriteAllText(
                    Path.Combine(dayDirectory, "rollout-test.jsonl"),
                    CreateEvent(capturedAt, 61) + Environment.NewLine);

                Assert.Equal("立即扫描", window.ScanNowMenuItem.Header);
                window.ScanNowMenuItem.RaiseEvent(
                    new RoutedEventArgs(MenuItem.ClickEvent, window.ScanNowMenuItem));
                Assert.True(PumpUntil(window.Dispatcher, () => viewModel.Windows.Count == 1));
                Assert.Equal(39d, viewModel.Windows[0].RemainingPercent);

                window.Left = 321;
                window.Top = 123;
                window.Dispatcher.Invoke(window.UpdateLayout, DispatcherPriority.Loaded);
                Assert.Equal("退出", window.ExitMenuItem.Header);
                window.ExitMenuItem.RaiseEvent(
                    new RoutedEventArgs(MenuItem.ClickEvent, window.ExitMenuItem));
                Assert.True(PumpUntil(window.Dispatcher, () => !window.IsVisible));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                application?.Shutdown();
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(10)), "WPF smoke test timed out.");
        thread.Join();
        Assert.Null(failure);
        Assert.True(File.Exists(settingsPath));
        using (var document = JsonDocument.Parse(File.ReadAllText(settingsPath)))
        {
            Assert.InRange(document.RootElement.GetProperty("left").GetDouble(), 320.5, 321.5);
            Assert.InRange(document.RootElement.GetProperty("top").GetDouble(), 122.5, 123.5);
        }

        if (Directory.Exists(settingsDirectory))
        {
            Directory.Delete(settingsDirectory, recursive: true);
        }
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

    private static bool PumpUntil(
        Dispatcher dispatcher,
        Func<bool> condition,
        int timeoutMilliseconds = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }

        return condition();
    }

    private sealed class WindowedDesktopHost : IDesktopHost
    {
        public bool EnsureAttached(nint widgetHandle) => false;
    }
}
