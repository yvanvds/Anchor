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
