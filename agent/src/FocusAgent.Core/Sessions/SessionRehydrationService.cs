using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FocusAgent.Core.Sessions;

/// <summary>
/// Bridges agent startup with the backend's rejoinable-sessions endpoint
/// (#54). When the connection first reports <c>Connected</c>, fetches the
/// caller's currently-joined sessions and asks <see cref="SessionCoordinator"/>
/// to silently rejoin each one (no toast — the student already consented
/// before the crash that took the previous process down).
///
/// The trigger is driven externally via <see cref="NotifyConnected"/> rather
/// than by subscribing to <c>ConnectionManager</c> directly, because that
/// class lives in the App layer (it owns WAM and the SignalR transport).
/// Keeping rehydration in Core lets it be unit-tested without WinUI on the
/// stack.
///
/// "Run-once-per-process" semantics:
///   * The first successful rehydration sets a latch; subsequent reconnects
///     don't re-fetch (the agent stayed in the session group across a
///     transient hub drop — no rejoin needed).
///   * If the first attempt throws (e.g. network failure between hub
///     Connected and the REST call), the latch stays unset so the next
///     <see cref="NotifyConnected"/> tries again.
/// </summary>
public sealed class SessionRehydrationService
{
    private readonly ISessionRehydrationClient _client;
    private readonly SessionCoordinator _coordinator;
    private readonly ILogger<SessionRehydrationService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _completed;

    public SessionRehydrationService(
        ISessionRehydrationClient client,
        SessionCoordinator coordinator,
        ILogger<SessionRehydrationService>? log = null)
    {
        _client = client;
        _coordinator = coordinator;
        _log = log ?? NullLogger<SessionRehydrationService>.Instance;
    }

    /// <summary>True once a rehydration pass completed without throwing.</summary>
    public bool HasRehydrated
    {
        get
        {
            _gate.Wait();
            try { return _completed; }
            finally { _gate.Release(); }
        }
    }

    /// <summary>
    /// Hook called by the App layer when its connection manager transitions to
    /// Connected. Async-void-safe: exceptions are caught and logged.
    /// </summary>
    public async Task NotifyConnectedAsync(CancellationToken ct = default)
    {
        if (_completed) return;
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            // Another NotifyConnectedAsync is already running. The semaphore
            // serialises overlapping triggers so we never fire two parallel
            // /sessions/rejoinable fetches.
            return;
        }
        try
        {
            if (_completed) return;
            await RehydrateAsync(ct).ConfigureAwait(false);
            _completed = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Session rehydration failed; the student's previous session won't be re-attached until the teacher starts a new one or the agent is restarted.");
            // Leave _completed false so the next NotifyConnectedAsync retries.
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RehydrateAsync(CancellationToken ct)
    {
        var sessions = await _client.GetRejoinableSessionsAsync(ct).ConfigureAwait(false);
        if (sessions.Count == 0)
        {
            _log.LogDebug("No rejoinable sessions for this student.");
            return;
        }

        _log.LogInformation(
            "Rehydration: backend reports {Count} rejoinable session(s); attempting silent rejoin.",
            sessions.Count);

        foreach (var payload in sessions)
        {
            if (ct.IsCancellationRequested) return;
            // SessionCoordinator.RejoinAsync is itself idempotent on
            // already-joined sessions, so even if a SessionStarted broadcast
            // raced us we won't double-join.
            await _coordinator.RejoinAsync(payload, ct).ConfigureAwait(false);
        }
    }
}
