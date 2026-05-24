using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using FocusAgent.Core.Focus;
using FocusAgent.Native.Win32;

namespace FocusAgent.Native;

[SupportedOSPlatform("windows")]
public sealed class AppIdentifier : IAppIdentifier
{
    private readonly ConcurrentDictionary<int, AppInfo> _cache = new();

    public AppInfo? Identify(nint windowHandle, int processId)
    {
        if (processId <= 0)
            return null;

        if (_cache.TryGetValue(processId, out var cached))
            return cached;

        var info = BuildAppInfo(processId);
        if (info is not null)
            _cache[processId] = info;
        return info;
    }

    public void Forget(int processId) => _cache.TryRemove(processId, out _);

    public void Clear() => _cache.Clear();

    public bool LaunchOrActivate(AllowedAppRule rule)
    {
        if (rule is null || string.IsNullOrWhiteSpace(rule.Value))
            return false;

        switch (rule.MatchKind)
        {
            case AllowedAppMatchKind.ExecutablePath:
                return ActivateByPath(rule.Value) || Launch(rule.Value);

            case AllowedAppMatchKind.ProcessName:
                return ActivateByProcessName(rule.Value) || Launch(rule.Value);

            case AllowedAppMatchKind.Publisher:
                // No path to launch; activating signed-by-publisher would require
                // enumerating every process and reading its signature. Skip for v1.
                return false;

            default:
                return false;
        }
    }

    private static bool ActivateByPath(string exePath)
    {
        var normalized = TryNormalizePath(exePath);
        if (normalized is null) return false;
        foreach (var proc in Process.GetProcesses())
        {
            using (proc)
            {
                string? path;
                try { path = proc.MainModule?.FileName; } catch { continue; }
                if (path is null) continue;
                if (!string.Equals(TryNormalizePath(path), normalized, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (TryActivateMainWindow(proc))
                    return true;
            }
        }
        return false;
    }

    private static bool ActivateByProcessName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];
        Process[] matches;
        try { matches = Process.GetProcessesByName(trimmed); } catch { return false; }
        try
        {
            foreach (var proc in matches)
            {
                if (TryActivateMainWindow(proc))
                    return true;
            }
        }
        finally
        {
            foreach (var p in matches) p.Dispose();
        }
        return false;
    }

    private static bool TryActivateMainWindow(Process proc)
    {
        nint hwnd;
        try { hwnd = proc.MainWindowHandle; } catch { return false; }
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(hwnd);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool Launch(string exePathOrName)
    {
        try
        {
            var psi = new ProcessStartInfo(exePathOrName) { UseShellExecute = true };
            using var p = Process.Start(psi);
            return p is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryNormalizePath(string path)
    {
        try { return Path.GetFullPath(path); } catch { return null; }
    }

    private static AppInfo? BuildAppInfo(int processId)
    {
        string? exePath = TryGetExecutablePath(processId);
        string processName = TryGetProcessName(processId, exePath);
        if (string.IsNullOrEmpty(processName))
            return null;

        string? publisher = null;
        if (!string.IsNullOrEmpty(exePath))
        {
            try
            {
                publisher = AuthenticodeReader.ReadPublisher(exePath);
            }
            catch
            {
                publisher = null;
            }
        }

        return new AppInfo(processName, exePath, publisher);
    }

    private static string? TryGetExecutablePath(int processId)
    {
        // Try QueryFullProcessImageName first — works for most processes
        // without requiring PROCESS_VM_READ. Falls back to Process.MainModule.
        var hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (hProc != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (NativeMethods.QueryFullProcessImageNameW(hProc, 0, sb, ref size))
                    return sb.ToString(0, (int)size);
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
        }

        try
        {
            using var proc = Process.GetProcessById(processId);
            return proc.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string TryGetProcessName(int processId, string? exePath)
    {
        if (!string.IsNullOrEmpty(exePath))
            return Path.GetFileNameWithoutExtension(exePath) ?? "";
        try
        {
            using var proc = Process.GetProcessById(processId);
            return proc.ProcessName;
        }
        catch
        {
            return "";
        }
    }
}
