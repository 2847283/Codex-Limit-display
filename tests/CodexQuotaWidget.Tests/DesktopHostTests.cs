using CodexQuotaWidget.Interop;

namespace CodexQuotaWidget.Tests;

public sealed class DesktopHostTests
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    [Fact]
    public void EnsureAttached_WhenDesktopIsForeground_RaisesTopLevelWidget()
    {
        var native = CreateDesktopTopology();
        native.ForegroundWindow = nint.Zero;

        var attached = new DesktopHost(native).EnsureAttached((nint)100);

        Assert.True(attached);
        Assert.Empty(native.ParentChanges);

        var position = Assert.Single(native.PositionChanges);
        Assert.Equal((nint)100, position.Window);
        Assert.Equal(nint.Zero, position.InsertAfter);
        Assert.Equal(0, position.X);
        Assert.Equal(0, position.Y);
        Assert.Equal(0, position.Width);
        Assert.Equal(0, position.Height);
        Assert.Equal(
            SwpNoSize | SwpNoMove | SwpNoActivate | SwpShowWindow,
            position.Flags);
    }

    [Fact]
    public void EnsureAttached_WhenAnotherAppIsForeground_LowersWidgetBehindIt()
    {
        var native = CreateDesktopTopology();
        native.ForegroundWindow = (nint)999;

        var attached = new DesktopHost(native).EnsureAttached((nint)100);

        Assert.True(attached);
        Assert.Empty(native.ParentChanges);
        Assert.Equal((nint)999, Assert.Single(native.PositionChanges).InsertAfter);
    }

    [Fact]
    public void EnsureAttached_RaisesOnceEachTimeDesktopReturnsToForeground()
    {
        var native = CreateDesktopTopology();
        var host = new DesktopHost(native);

        native.ForegroundWindow = nint.Zero;
        Assert.True(host.EnsureAttached((nint)100));
        Assert.True(host.EnsureAttached((nint)100));

        native.ForegroundWindow = (nint)999;
        Assert.True(host.EnsureAttached((nint)100));
        native.ForegroundWindow = (nint)30;
        Assert.True(host.EnsureAttached((nint)100));

        Assert.Collection(
            native.PositionChanges,
            change => Assert.Equal(nint.Zero, change.InsertAfter),
            change => Assert.Equal((nint)999, change.InsertAfter),
            change => Assert.Equal(nint.Zero, change.InsertAfter));
    }

    private static FakeDesktopWindowApi CreateDesktopTopology()
    {
        var native = new FakeDesktopWindowApi
        {
            Progman = (nint)10,
            TopLevelWindows = [(nint)20]
        };
        native.ChildWindows[((nint)20, "SHELLDLL_DefView")] = (nint)30;
        return native;
    }

    private sealed class FakeDesktopWindowApi : IDesktopWindowApi
    {
        public nint Progman { get; init; }

        public IReadOnlyList<nint> TopLevelWindows { get; init; } = [];

        public nint ForegroundWindow { get; set; }

        public Dictionary<(nint Parent, string ClassName), nint> ChildWindows { get; } = [];

        public Dictionary<nint, nint> Parents { get; } = [];

        public List<(nint Child, nint Parent)> ParentChanges { get; } = [];

        public List<WindowPositionChange> PositionChanges { get; } = [];

        public nint FindTopLevelWindow(string className) => Progman;

        public void RequestWorkerWindow(nint progman)
        {
        }

        public IReadOnlyList<nint> EnumerateTopLevelWindows() => TopLevelWindows;

        public nint FindChildWindow(nint parent, string className) =>
            ChildWindows.GetValueOrDefault((parent, className));

        public nint GetForegroundWindow() => ForegroundWindow;

        public bool IsWindow(nint window) => window != nint.Zero;

        public nint GetParent(nint window) => Parents.GetValueOrDefault(window);

        public bool SetWindowPosition(
            nint window,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags)
        {
            PositionChanges.Add(new WindowPositionChange(
                window,
                insertAfter,
                x,
                y,
                width,
                height,
                flags));
            return true;
        }
    }

    private sealed record WindowPositionChange(
        nint Window,
        nint InsertAfter,
        int X,
        int Y,
        int Width,
        int Height,
        uint Flags);
}
