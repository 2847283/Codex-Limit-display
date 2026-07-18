using System.Runtime.InteropServices;

namespace CodexQuotaWidget.Interop;

public interface IDesktopHost
{
    bool EnsureAttached(nint widgetHandle);
}

internal interface IDesktopWindowApi
{
    nint FindTopLevelWindow(string className);

    void RequestWorkerWindow(nint progman);

    IReadOnlyList<nint> EnumerateTopLevelWindows();

    nint FindChildWindow(nint parent, string className);

    bool IsWindow(nint window);

    nint GetParent(nint window);

    nint GetForegroundWindow();

    bool SetWindowPosition(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}

public sealed class DesktopHost : IDesktopHost
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private readonly IDesktopWindowApi _native;
    private nint _knownHost;
    private nint _knownDefView;
    private bool _hasObservedForeground;
    private bool _wasDesktopForeground;

    public DesktopHost()
        : this(new Win32DesktopWindowApi())
    {
    }

    internal DesktopHost(IDesktopWindowApi native)
    {
        _native = native;
    }

    public bool EnsureAttached(nint widgetHandle)
    {
        if (widgetHandle == nint.Zero)
        {
            return false;
        }

        if (_knownHost == nint.Zero ||
            _knownDefView == nint.Zero ||
            !_native.IsWindow(_knownHost) ||
            !_native.IsWindow(_knownDefView))
        {
            if (!TryRefreshDesktopHandles())
            {
                _wasDesktopForeground = false;
                return false;
            }
        }

        var foreground = _native.GetForegroundWindow();
        var desktopForeground = IsDesktopForeground(foreground, widgetHandle);
        var zOrderUpdated = true;
        if (desktopForeground && (!_hasObservedForeground || !_wasDesktopForeground))
        {
            zOrderUpdated = _native.SetWindowPosition(
                widgetHandle,
                nint.Zero,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoActivate | SwpShowWindow);
        }
        else if (!desktopForeground && (!_hasObservedForeground || _wasDesktopForeground))
        {
            zOrderUpdated = _native.SetWindowPosition(
                widgetHandle,
                foreground,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoActivate | SwpShowWindow);
        }

        _hasObservedForeground = true;
        _wasDesktopForeground = desktopForeground;
        return zOrderUpdated;
    }

    private bool IsDesktopForeground(nint foreground, nint widgetHandle)
    {
        if (foreground == nint.Zero ||
            foreground == widgetHandle ||
            foreground == _knownHost ||
            foreground == _knownDefView)
        {
            return true;
        }

        var foregroundParent = _native.GetParent(foreground);
        return foregroundParent == widgetHandle ||
               foregroundParent == _knownHost ||
               foregroundParent == _knownDefView;
    }

    private bool TryRefreshDesktopHandles()
    {
        var progman = _native.FindTopLevelWindow("Progman");
        if (progman != nint.Zero)
        {
            _native.RequestWorkerWindow(progman);
        }

        foreach (var topLevel in _native.EnumerateTopLevelWindows())
        {
            var defView = _native.FindChildWindow(topLevel, "SHELLDLL_DefView");
            if (defView != nint.Zero)
            {
                _knownHost = topLevel;
                _knownDefView = defView;
                return true;
            }
        }

        _knownHost = nint.Zero;
        _knownDefView = nint.Zero;
        return false;
    }
}

internal sealed class Win32DesktopWindowApi : IDesktopWindowApi
{
    private const uint SpawnWorkerMessage = 0x052C;
    private static readonly nint RaisedDesktopRequest = (nint)0xD;
    private static readonly nint EnableRaisedDesktop = (nint)0x1;
    private const uint SmtoAbortIfHung = 0x0002;

    public nint FindTopLevelWindow(string className) =>
        FindWindow(className, null);

    public void RequestWorkerWindow(nint progman) =>
        _ = SendMessageTimeout(
            progman,
            SpawnWorkerMessage,
            RaisedDesktopRequest,
            EnableRaisedDesktop,
            SmtoAbortIfHung,
            1000,
            out _);

    public IReadOnlyList<nint> EnumerateTopLevelWindows()
    {
        var windows = new List<nint>();
        _ = EnumWindows((window, _) =>
        {
            windows.Add(window);
            return true;
        }, nint.Zero);
        return windows;
    }

    public nint FindChildWindow(nint parent, string className) =>
        FindWindowEx(parent, nint.Zero, className, null);

    public bool IsWindow(nint window) => IsWindowNative(window);

    public nint GetParent(nint window) => GetParentNative(window);

    public nint GetForegroundWindow() => GetForegroundWindowNative();

    public bool SetWindowPosition(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags) =>
        SetWindowPos(window, insertAfter, x, y, width, height, flags);

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowEx(
        nint parent,
        nint childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetParent")]
    private static extern nint GetParentNative(nint window);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint GetForegroundWindowNative();

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowNative(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeout,
        out nint result);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
