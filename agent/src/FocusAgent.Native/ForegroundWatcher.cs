using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text;
using FocusAgent.Core.Focus;
using FocusAgent.Native.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FocusAgent.Native;

/// <summary>
/// Wraps <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c>. Marshals raw OS
/// callbacks through a <see cref="SynchronizationContext"/> so subscribers
/// see <see cref="Changed"/> on the agent UI thread, and resolves the active
/// HWND/PID into an <see cref="AppInfo"/> via the injected
/// <see cref="IAppIdentifier"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ForegroundWatcher : IForegroundWatcher
{
    private readonly IAppIdentifier _identifier;
    private readonly SynchronizationContext? _syncContext;
    private readonly ILogger<ForegroundWatcher> _log;
    private readonly object _gate = new();
    // The native hook holds a function pointer to the delegate. Holding the
    // delegate in a field is essential — otherwise the GC collects it and
    // we crash the foreground thread on the next focus change.
    private readonly NativeMethods.WinEventDelegate _callback;

    private nint _hook;
    private bool _disposed;

    public ForegroundWatcher(
        IAppIdentifier identifier,
        SynchronizationContext? syncContext = null,
        ILogger<ForegroundWatcher>? log = null)
    {
        _identifier = identifier;
        _syncContext = syncContext ?? SynchronizationContext.Current;
        _log = log ?? NullLogger<ForegroundWatcher>.Instance;
        _callback = OnWinEvent;
    }

    public event Action<ForegroundChange>? Changed;

    public bool IsRunning
    {
        get { lock (_gate) return _hook != IntPtr.Zero; }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // SetWinEventHook with WINEVENT_OUTOFCONTEXT delivers callbacks via the
        // registering thread's message queue. SessionJoined fires on a SignalR
        // worker after async-void continuations land in the thread pool, so
        // registering inline would silently no-op (issue #64). Marshal to the
        // captured UI sync context so the hook is owned by a pumping thread.
        RunOnSyncContext(StartCore);
    }

    public void Stop() => RunOnSyncContext(StopCore);

    private void StartCore()
    {
        lock (_gate)
        {
            if (_hook != IntPtr.Zero)
                return;
            _hook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                hmodWinEventProc: IntPtr.Zero,
                _callback,
                idProcess: 0,
                idThread: 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
            if (_hook == IntPtr.Zero)
            {
                _log.LogError(
                    "SetWinEventHook returned NULL on thread {ThreadId}",
                    Environment.CurrentManagedThreadId);
                return;
            }
            _log.LogInformation(
                "ForegroundWatcher started (hook=0x{Hook:X} thread={ThreadId})",
                _hook, Environment.CurrentManagedThreadId);
        }
    }

    private void StopCore()
    {
        nint hook;
        lock (_gate)
        {
            hook = _hook;
            _hook = IntPtr.Zero;
        }
        if (hook != IntPtr.Zero)
        {
            if (!NativeMethods.UnhookWinEvent(hook))
                _log.LogWarning(
                    "UnhookWinEvent returned false (hook=0x{Hook:X} thread={ThreadId})",
                    hook, Environment.CurrentManagedThreadId);
            else
                _log.LogInformation(
                    "ForegroundWatcher stopped (thread={ThreadId})",
                    Environment.CurrentManagedThreadId);
        }
    }

    private void RunOnSyncContext(Action action)
    {
        if (_syncContext is null || ReferenceEquals(SynchronizationContext.Current, _syncContext))
        {
            action();
            return;
        }
        _log.LogDebug(
            "ForegroundWatcher: marshaling {Action} from thread {CallingThreadId} onto captured SynchronizationContext",
            action.Method.Name, Environment.CurrentManagedThreadId);

        // WinUI 3's DispatcherQueueSynchronizationContext refuses Send (throws
        // NotSupportedException) — emulate synchronous semantics with Post +
        // ManualResetEventSlim so Start/Stop stay synchronous to the caller
        // while the hook is owned by the pumping UI thread.
        using var done = new ManualResetEventSlim(false);
        ExceptionDispatchInfo? captured = null;
        _syncContext.Post(_ =>
        {
            try { action(); }
            catch (Exception ex) { captured = ExceptionDispatchInfo.Capture(ex); }
            finally { done.Set(); }
        }, state: null);
        done.Wait();
        captured?.Throw();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Logged before any filter so a silent watcher (issue #64) is
        // distinguishable from "callback fires but every event is filtered."
        _log.LogTrace(
            "OnWinEvent: event=0x{EventType:X} hwnd=0x{Hwnd:X} obj={IdObject} child={IdChild} thread={ThreadId}",
            eventType, hwnd, idObject, idChild, Environment.CurrentManagedThreadId);

        // OBJID_WINDOW = 0; ignore child-object foreground notifications.
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero)
            return;
        if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND)
            return;

        ForegroundChange? change;
        try
        {
            change = BuildChange(hwnd);
        }
        catch (Exception ex)
        {
            // Never throw out of the hook callback — Windows handles it badly.
            _log.LogError(ex, "ForegroundWatcher: BuildChange threw for hwnd 0x{Hwnd:X}", hwnd);
            return;
        }

        if (change is null)
            return;

        // Marshal to the captured SynchronizationContext (typically the UI
        // thread) before firing — subscribers can safely touch UI objects.
        if (_syncContext is not null)
            _syncContext.Post(_ => RaiseChanged(change), state: null);
        else
            RaiseChanged(change);
    }

    private void RaiseChanged(ForegroundChange change)
    {
        try
        {
            Changed?.Invoke(change);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ForegroundWatcher subscriber threw for {ProcessName}", change.App.ProcessName);
        }
    }

    private ForegroundChange? BuildChange(nint hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
            return null;

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var rawPid);
        var pid = (int)rawPid;
        var info = _identifier.Identify(hwnd, pid);
        if (info is null)
            return null;
        var title = GetWindowTitle(hwnd);
        return new ForegroundChange(info, title, pid, hwnd);
    }

    private static string? GetWindowTitle(nint hwnd)
    {
        var len = NativeMethods.GetWindowTextLengthW(hwnd);
        if (len <= 0) return null;
        var sb = new StringBuilder(len + 1);
        var written = NativeMethods.GetWindowTextW(hwnd, sb, sb.Capacity);
        return written > 0 ? sb.ToString(0, written) : null;
    }
}
