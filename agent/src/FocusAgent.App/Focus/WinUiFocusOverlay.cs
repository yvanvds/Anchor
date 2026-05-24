using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FocusAgent.Core.Focus;
using FocusAgent.Native.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace FocusAgent.App.Focus;

/// <summary>
/// WinUI 3 host of the focus-enforcement overlay (issue #33). Lives across
/// the session's lifetime — Show/Hide flip <c>HWND_TOPMOST</c> via the
/// native helper; Close clears <c>HWND_NOTOPMOST</c> before tearing the
/// window down so the OS doesn't leave a phantom topmost handle.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WinUiFocusOverlay : IFocusOverlay, IDisposable
{
    private const int OverlayWidthDip = 480;
    private const int OverlayHeightDip = 320;

    private readonly DispatcherQueue _dispatcher;
    private readonly IAppIdentifier _launcher;
    private readonly ILogger<WinUiFocusOverlay> _log;
    private readonly object _gate = new();
    private FocusOverlayWindow? _window;
    private bool _disposed;

    public WinUiFocusOverlay(
        DispatcherQueue dispatcher,
        IAppIdentifier launcher,
        ILogger<WinUiFocusOverlay>? log = null)
    {
        _dispatcher = dispatcher;
        _launcher = launcher;
        _log = log ?? NullLogger<WinUiFocusOverlay>.Instance;
    }

    public void Show(IReadOnlyList<AllowedAppRule> allowedRules, string? blockedAppName)
    {
        if (_disposed) return;
        _dispatcher.TryEnqueue(() => ShowOnUiThread(allowedRules, blockedAppName));
    }

    public void Hide()
    {
        if (_disposed) return;
        _dispatcher.TryEnqueue(HideOnUiThread);
    }

    public void Close()
    {
        if (_disposed) return;
        _dispatcher.TryEnqueue(CloseOnUiThread);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { CloseOnUiThread(); } catch { /* shutdown best-effort */ }
    }

    private void ShowOnUiThread(IReadOnlyList<AllowedAppRule> allowedRules, string? blockedAppName)
    {
        try
        {
            FocusOverlayWindow window;
            lock (_gate)
            {
                window = _window ??= CreateWindow();
            }

            window.UpdateContent(allowedRules, blockedAppName);
            ApplyTopmostAndPosition(window);
            window.Activate();
            _log.LogInformation("Focus overlay shown with {RuleCount} allowed-app entries", allowedRules.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Focus overlay Show failed");
        }
    }

    private void HideOnUiThread()
    {
        try
        {
            FocusOverlayWindow? window;
            lock (_gate) window = _window;
            if (window is null) return;

            var hwnd = WindowNative.GetWindowHandle(window);
            var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
            appWindow.Hide();
            _log.LogDebug("Focus overlay hidden");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Focus overlay Hide failed");
        }
    }

    private void CloseOnUiThread()
    {
        FocusOverlayWindow? window;
        lock (_gate)
        {
            window = _window;
            _window = null;
        }
        if (window is null) return;
        try
        {
            // Clear HWND_TOPMOST before destroying so the OS doesn't leave a
            // phantom topmost slot in its Z-order (per #33 AC).
            var hwnd = WindowNative.GetWindowHandle(window);
            ClearTopmost(hwnd);
            window.Close();
            _log.LogDebug("Focus overlay closed");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Focus overlay Close failed");
        }
    }

    private FocusOverlayWindow CreateWindow()
    {
        var window = new FocusOverlayWindow(_launcher, _ => HideOnUiThread());
        window.Closed += (_, _) =>
        {
            lock (_gate)
            {
                if (ReferenceEquals(_window, window))
                    _window = null;
            }
        };
        return window;
    }

    private static void ApplyTopmostAndPosition(FocusOverlayWindow window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96.0;

        var width = (int)(OverlayWidthDip * scale);
        var height = (int)(OverlayHeightDip * scale);

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var x = workArea.X + (workArea.Width - width) / 2;
        var y = workArea.Y + (workArea.Height - height) / 2;

        appWindow.MoveAndResize(new RectInt32(x, y, width, height));

        // Belt-and-braces: OverlappedPresenter.IsAlwaysOnTop should already
        // map to HWND_TOPMOST, but #33 explicitly calls SetWindowPos out as
        // the contract — call it directly so the behaviour matches the spec
        // even if the WinUI mapping ever changes.
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private static void ClearTopmost(nint hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_NOTOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
