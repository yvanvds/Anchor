using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace FocusAgent.App.Sessions;

/// <summary>
/// Sizes a <see cref="Window"/> as a compact toast pinned to the top-right of
/// the primary monitor's work area, then shows it without stealing focus from
/// whatever the student is currently doing (per #31 acceptance criteria).
///
/// The XAML island that renders the window's content is only initialised on
/// <see cref="Window.Activate"/>; earlier versions of this file used raw
/// <c>ShowWindow(SW_SHOWNOACTIVATE)</c>, which made the HWND visible but
/// skipped the WinUI show path entirely — the XAML stayed unrendered, so the
/// toast never appeared. That's the root cause of #41. The fix is to
/// <see cref="Window.Activate"/> (forcing the XAML island to render) and
/// immediately restore foreground to whatever was active before, which the OS
/// allows because this thread just received focus.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class ToastWindowPositioner
{
    private const int ToastWidthDip = 380;
    private const int ToastHeightDip = 160;
    private const int ToastMarginDip = 24;

    public static void ConfigureAndShow(Window window)
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

        var width = (int)(ToastWidthDip * scale);
        var height = (int)(ToastHeightDip * scale);
        var margin = (int)(ToastMarginDip * scale);

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var x = workArea.X + workArea.Width - width - margin;
        var y = workArea.Y + margin;

        appWindow.MoveAndResize(new RectInt32(x, y, width, height));

        var originalForeground = GetForegroundWindow();
        window.Activate();
        if (originalForeground != IntPtr.Zero && originalForeground != hwnd)
            SetForegroundWindow(originalForeground);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
