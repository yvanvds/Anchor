using FocusAgent.Core.Dtos;
using FocusAgent.Core.Sessions;
using FocusAgent.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FocusAgent.Core.Tamper;

/// <summary>
/// Agent-as-witness InPrivate detection (#148). While the student is in a joined
/// session, polls the open Edge windows every
/// <see cref="SessionSettings.InPrivateScanIntervalSeconds"/> and reports a
/// <see cref="TamperKinds.InPrivateOpened"/> tamper event for each newly-seen
/// InPrivate window. This is the robust counterpart to the extension's in-browser
/// signal: the extension only witnesses InPrivate once it has been *allowed* in
/// InPrivate (design §5.4), so a student who leaves that toggle off escapes it —
/// the agent, watching from outside the browser, does not.
///
/// Same lifecycle model as <see cref="SessionHeartbeatService"/>: start the poll
/// loop on <see cref="SessionCoordinator.SessionJoined"/>, stop it on
/// <see cref="SessionCoordinator.SessionLeft"/>, so scans only run when there is
/// a joined session whose hub will accept the event. Soft enforcement: we surface
/// the attempt to the teacher, we don't prevent it.
/// </summary>
public sealed class InPrivateWitnessMonitor : IAsyncDisposable
{
    private readonly SessionCoordinator _coordinator;
    private readonly IBrowserWindowScanner _scanner;
    private readonly ITamperReporter _reporter;
    private readonly TimeProvider _clock;
    private readonly SessionSettings _settings;
    private readonly ILogger<InPrivateWitnessMonitor> _log;
    private readonly object _gate = new();

    private ITimer? _timer;
    private Guid? _sessionId;
    // Handles of InPrivate windows already reported for the current session, so a
    // window that stays open across many polls is reported exactly once. Cleared
    // on session start/stop. A window that closes is pruned, so reopening it
    // (even if Windows recycles the handle) reports again.
    private readonly HashSet<nint> _reported = new();
    private int _detectionCount;

    public InPrivateWitnessMonitor(
        SessionCoordinator coordinator,
        IBrowserWindowScanner scanner,
        ITamperReporter reporter,
        IOptions<SessionSettings> settings,
        TimeProvider? clock = null,
        ILogger<InPrivateWitnessMonitor>? log = null)
    {
        _coordinator = coordinator;
        _scanner = scanner;
        _reporter = reporter;
        _clock = clock ?? TimeProvider.System;
        _settings = settings.Value;
        _log = log ?? NullLogger<InPrivateWitnessMonitor>.Instance;

        _coordinator.SessionJoined += OnSessionJoined;
        _coordinator.SessionLeft += OnSessionLeft;
    }

    /// <summary>
    /// Number of InPrivate windows reported since this process started. Dev-only:
    /// surfaced on the status endpoint so the headless e2e can assert the
    /// agent-side detection fired without screenshotting a real InPrivate window.
    /// </summary>
    public int DetectionCount
    {
        get { lock (_gate) return _detectionCount; }
    }

    private void OnSessionJoined(object? sender, SessionStartedPayload payload) => StartFor(payload.SessionId);

    private void OnSessionLeft(object? sender, Guid sessionId) => Stop(sessionId);

    internal void StartFor(Guid sessionId)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.InPrivateScanIntervalSeconds));
        ITimer? toDispose;
        lock (_gate)
        {
            toDispose = _timer;
            _sessionId = sessionId;
            _reported.Clear();
            _timer = _clock.CreateTimer(
                _ => OnTimerFired(sessionId),
                state: null,
                dueTime: TimeSpan.Zero, // scan immediately so an InPrivate window already open at join is caught
                period: interval);
        }
        toDispose?.Dispose();
        _log.LogInformation(
            "InPrivateWitnessMonitor started for session {SessionId} (interval={IntervalSeconds}s)",
            sessionId, (int)interval.TotalSeconds);
    }

    internal void Stop(Guid sessionId)
    {
        ITimer? toDispose;
        lock (_gate)
        {
            if (_sessionId != sessionId) return;
            toDispose = _timer;
            _timer = null;
            _sessionId = null;
            _reported.Clear();
        }
        toDispose?.Dispose();
        _log.LogInformation("InPrivateWitnessMonitor stopped for session {SessionId}", sessionId);
    }

    private void OnTimerFired(Guid sessionId)
    {
        // The whole scan is best-effort: a probe or transport failure must never
        // crash the timer callback (an unhandled exception there tears the timer
        // down). Mirrors SessionHeartbeatService's swallow-and-continue posture.
        try
        {
            Scan(sessionId);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "InPrivate scan threw for session {SessionId}", sessionId);
        }
    }

    private void Scan(Guid sessionId)
    {
        // Re-check under the gate: a Stop() may have observed between this tick
        // being queued and running. Snapshot the windows outside the lock — the
        // scan touches Win32 and we don't want to hold the gate across it.
        lock (_gate)
        {
            if (_sessionId != sessionId) return;
        }

        var windows = _scanner.GetOpenBrowserWindows();

        var present = windows
            .Where(w => InPrivateDetection.IsInPrivateEdgeWindow(w.ProcessName, w.Title))
            .Select(w => w.Handle)
            .ToHashSet();

        var newlySeen = 0;
        lock (_gate)
        {
            if (_sessionId != sessionId) return;
            // Prune windows that have since closed, so a later reopen reports anew.
            _reported.IntersectWith(present);
            foreach (var handle in present)
            {
                if (_reported.Add(handle))
                {
                    newlySeen++;
                    _detectionCount++;
                }
            }
        }

        // One report per newly-seen InPrivate window. They carry no per-window
        // payload (the kind is the whole signal), so a count is all we need.
        for (var i = 0; i < newlySeen; i++)
            _ = ReportAsync(sessionId);
    }

    private async Task ReportAsync(Guid sessionId)
    {
        _log.LogWarning(
            "InPrivate window detected during session {SessionId} — reporting {Kind}.",
            sessionId, TamperKinds.InPrivateOpened);
        try
        {
            await _reporter.ReportAsync(sessionId, TamperKinds.InPrivateOpened).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort like every other tamper report: a failed send must not
            // stop the poll loop. The next poll won't re-report this window (it's
            // already in _reported), matching the extension's fire-and-forget
            // reportTamper — the teacher sees it if it reached the backend.
            _log.LogWarning(ex, "Reporting {Kind} failed for session {SessionId}",
                TamperKinds.InPrivateOpened, sessionId);
        }
    }

    public ValueTask DisposeAsync()
    {
        _coordinator.SessionJoined -= OnSessionJoined;
        _coordinator.SessionLeft -= OnSessionLeft;
        ITimer? toDispose;
        lock (_gate)
        {
            toDispose = _timer;
            _timer = null;
            _sessionId = null;
            _reported.Clear();
        }
        toDispose?.Dispose();
        return ValueTask.CompletedTask;
    }
}
