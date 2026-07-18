using System.IO;
using System.Windows;
using CodexQuotaWidget.Interop;
using CodexQuotaWidget.Services;
using CodexQuotaWidget.ViewModels;

namespace CodexQuotaWidget;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sessionsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "sessions");
        var watcher = new SessionQuotaWatcher(sessionsRoot, new RolloutQuotaReader());
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow(
            watcher,
            viewModel,
            new CodexProcessDetector(),
            WidgetSettingsStore.CreateDefault(),
            new DesktopHost());
        MainWindow = window;
        window.Show();
    }
}
