using System.Runtime.Versioning;
using System.Text;
using FocusAgent.Core.Focus;
using FocusAgent.Core.Tamper;
using FocusAgent.Native.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FocusAgent.Native;

/// <summary>
/// Enumerates titled top-level windows via <c>EnumWindows</c> and resolves each
/// to its owning process name + window title, feeding the agent's InPrivate
/// witness (#148). Unlike <see cref="WindowEnumerator"/> (which gates on the
/// alt-tab visibility rules for the focus sweep), this keeps any window that
/// carries a title — a minimized or background InPrivate window is still tamper
/// worth reporting — and the InPrivate decision itself lives in the pure
/// <see cref="InPrivateDetection"/>, not here.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BrowserWindowScanner : IBrowserWindowScanner
{
    private readonly IAppIdentifier _identifier;
    private readonly ILogger<BrowserWindowScanner> _log;

    public BrowserWindowScanner(IAppIdentifier identifier, ILogger<BrowserWindowScanner>? log = null)
    {
        _identifier = identifier;
        _log = log ?? NullLogger<BrowserWindowScanner>.Instance;
    }

    public IReadOnlyList<BrowserWindow> GetOpenBrowserWindows()
    {
        var windows = new List<BrowserWindow>();
        // EnumWindows is synchronous — the list is fully built by the time it
        // returns. Never throw out of the callback (Windows handles it badly);
        // swallow per-window probe failures and keep enumerating.
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (TryResolve(hwnd) is { } w)
                    windows.Add(w);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "BrowserWindowScanner: skipping hwnd 0x{Hwnd:X} after probe threw", hwnd);
            }
            return true; // keep enumerating
        }, IntPtr.Zero);
        return windows;
    }

    private BrowserWindow? TryResolve(nint hwnd)
    {
        // Edge keeps many hidden, title-less helper windows; a non-empty title is
        // a cheap pre-filter that drops those before the costlier process probe.
        var titleLength = NativeMethods.GetWindowTextLengthW(hwnd);
        if (titleLength <= 0)
            return null;

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var rawPid);
        var info = _identifier.Identify(hwnd, (int)rawPid);
        if (info is null)
            return null;

        var title = ReadTitle(hwnd, titleLength);
        return new BrowserWindow(hwnd, info.ProcessName, title);
    }

    private static string ReadTitle(nint hwnd, int titleLength)
    {
        // +1 for the null terminator GetWindowTextW writes.
        var sb = new StringBuilder(titleLength + 1);
        var copied = NativeMethods.GetWindowTextW(hwnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : string.Empty;
    }
}
