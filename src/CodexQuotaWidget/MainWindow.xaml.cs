using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CodexQuotaWidget.Interop;
using CodexQuotaWidget.Services;
using CodexQuotaWidget.ViewModels;

namespace CodexQuotaWidget;

public partial class MainWindow : Window
{
    private readonly SessionQuotaWatcher _watcher;
    private readonly MainWindowViewModel _viewModel;
    private readonly CodexProcessDetector _processDetector;
    private readonly WidgetSettingsStore _settingsStore;
    private readonly IDesktopHost _desktopHost;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _desktopTimer;
    private nint _windowHandle;
    private bool _started;
    private bool _disposed;

    public MainWindow(
        SessionQuotaWatcher watcher,
        MainWindowViewModel viewModel,
        CodexProcessDetector processDetector,
        WidgetSettingsStore settingsStore,
        IDesktopHost desktopHost)
    {
        InitializeComponent();
        _watcher = watcher;
        _viewModel = viewModel;
        _processDetector = processDetector;
        _settingsStore = settingsStore;
        _desktopHost = desktopHost;
        DataContext = viewModel;

        _watcher.SnapshotChanged += Watcher_SnapshotChanged;
        _watcher.StatusChanged += Watcher_StatusChanged;
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromMinutes(1),
            DispatcherPriority.Background,
            RefreshTimer_Tick,
            Dispatcher);
        _refreshTimer.Stop();

        _desktopTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            DesktopTimer_Tick,
            Dispatcher);
        _desktopTimer.Stop();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        RestoreVisiblePosition();
        _ = _desktopHost.EnsureAttached(_windowHandle);
        _desktopTimer.Start();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        RefreshProcessState();
        _refreshTimer.Start();
        _ = Task.Run(_watcher.Start);
    }

    private void Watcher_SnapshotChanged(object? sender, Models.QuotaSnapshot snapshot) =>
        Dispatcher.BeginInvoke(() => _viewModel.ApplySnapshot(snapshot));

    private void Watcher_StatusChanged(object? sender, string status) =>
        Dispatcher.BeginInvoke(() => _viewModel.SetServiceStatus(status));

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _viewModel.Refresh();
        RefreshProcessState();
    }

    private void DesktopTimer_Tick(object? sender, EventArgs e) =>
        _ = _desktopHost.EnsureAttached(_windowHandle);

    private void RefreshProcessState() =>
        _viewModel.SetProcessState(_processDetector.GetState());

    private void ScanNow_Click(object sender, RoutedEventArgs e) =>
        _ = Task.Run(_watcher.ScanNow);

    private void Exit_Click(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button may have been released between the event and DragMove.
        }
        finally
        {
            SavePosition();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e) =>
        SavePosition();

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        DisposeServices();
    }

    private void RestoreVisiblePosition()
    {
        var virtualBounds = new WidgetBounds(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var workArea = SystemParameters.WorkArea;
        var primaryWorkArea = new WidgetBounds(
            workArea.Left,
            workArea.Top,
            workArea.Width,
            workArea.Height);
        var position = _settingsStore.Load(
            virtualBounds,
            primaryWorkArea,
            Width,
            ActualHeight > 0 ? ActualHeight : 240);
        Left = position.Left;
        Top = position.Top;
    }

    private void SavePosition()
    {
        var position = new WidgetPosition(Left, Top);
        if (_windowHandle != nint.Zero && IsLoaded)
        {
            try
            {
                var screenPixels = PointToScreen(new Point(0, 0));
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget is not null)
                {
                    var screenDips = source.CompositionTarget.TransformFromDevice.Transform(screenPixels);
                    position = new WidgetPosition(screenDips.X, screenDips.Y);
                }
            }
            catch (InvalidOperationException)
            {
                // The native source may disappear during system shutdown.
            }
        }

        _settingsStore.Save(position);
    }

    private void DisposeServices()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshTimer.Stop();
        _desktopTimer.Stop();
        _watcher.SnapshotChanged -= Watcher_SnapshotChanged;
        _watcher.StatusChanged -= Watcher_StatusChanged;
        _watcher.Dispose();
    }
}
