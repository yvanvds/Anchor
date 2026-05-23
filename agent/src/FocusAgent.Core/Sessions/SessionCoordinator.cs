using FocusAgent.Core.Dtos;
using FocusAgent.Core.Realtime;
using FocusAgent.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FocusAgent.Core.Sessions;

public sealed class SessionCoordinator : IAsyncDisposable
{
    private readonly ISessionHubConnection _hub;
    private readonly ISessionUiHost _ui;
    private readonly TimeProvider _clock;
    private readonly RealtimeSettings _settings;
    private readonly ILogger<SessionCoordinator> _log;
    private readonly object _gate = new();

    private JoinConfirmation? _active;
    private Guid? _activeSessionId;

    public SessionCoordinator(
        ISessionHubConnection hub,
        ISessionUiHost ui,
        IOptions<RealtimeSettings> settings,
        TimeProvider? clock = null,
        ILogger<SessionCoordinator>? log = null)
    {
        _hub = hub;
        _ui = ui;
        _clock = clock ?? TimeProvider.System;
        _settings = settings.Value;
        _log = log ?? NullLogger<SessionCoordinator>.Instance;

        _hub.SessionStarted += OnSessionStarted;
        _hub.SessionEnded += OnSessionEnded;
    }

    public JoinConfirmation? ActiveConfirmation
    {
        get { lock (_gate) return _active; }
    }

    public Guid? ActiveSessionId
    {
        get { lock (_gate) return _activeSessionId; }
    }

    private async void OnSessionStarted(object? sender, SessionStartedPayload payload)
    {
        try
        {
            await HandleSessionStartedAsync(payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed handling SessionStarted for {SessionId}", payload.SessionId);
        }
    }

    internal async Task HandleSessionStartedAsync(SessionStartedPayload payload, CancellationToken ct = default)
    {
        // SessionStartedPayload does not yet carry the teacher's display name.
        // Until the dashboard-polish issue lands it, surface a generic label.
        const string teacherPlaceholder = "Your teacher";

        var confirmation = new JoinConfirmation(payload, teacherPlaceholder, _settings.JoinConfirmationDuration, _clock);

        lock (_gate)
        {
            _active?.Abort();
            _active = confirmation;
            _activeSessionId = payload.SessionId;
        }

        _log.LogInformation("Session {SessionId} started; awaiting student confirmation", payload.SessionId);

        var decision = await _ui.ShowJoinConfirmationAsync(confirmation, ct).ConfigureAwait(false);

        switch (decision)
        {
            case JoinDecision.Confirmed:
                try
                {
                    await _hub.JoinSessionAsync(payload.SessionId, joinCode: null, ct).ConfigureAwait(false);
                    _log.LogInformation("Joined session {SessionId}", payload.SessionId);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "JoinSession failed for {SessionId}", payload.SessionId);
                }
                break;
            case JoinDecision.Declined:
                try
                {
                    await _hub.LeaveSessionAsync(payload.SessionId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "LeaveSession on decline failed for {SessionId}", payload.SessionId);
                }
                _log.LogInformation("Declined session {SessionId}", payload.SessionId);
                break;
            case JoinDecision.Aborted:
                _log.LogInformation("Session {SessionId} confirmation aborted", payload.SessionId);
                break;
        }

        lock (_gate)
        {
            if (ReferenceEquals(_active, confirmation))
            {
                _active = null;
                if (decision != JoinDecision.Confirmed)
                    _activeSessionId = null;
            }
        }
    }

    private void OnSessionEnded(object? sender, Guid sessionId)
    {
        try
        {
            HandleSessionEnded(sessionId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed handling SessionEnded for {SessionId}", sessionId);
        }
    }

    internal void HandleSessionEnded(Guid sessionId)
    {
        JoinConfirmation? toCancel = null;
        bool matched;
        lock (_gate)
        {
            if (_active is not null && _active.Payload.SessionId == sessionId)
            {
                toCancel = _active;
                _active = null;
            }
            matched = _activeSessionId == sessionId;
            if (matched)
                _activeSessionId = null;
        }
        toCancel?.Abort();
        if (matched)
            _ui.DismissJoinConfirmation();
        _log.LogInformation("Session {SessionId} ended", sessionId);
    }

    public async ValueTask DisposeAsync()
    {
        _hub.SessionStarted -= OnSessionStarted;
        _hub.SessionEnded -= OnSessionEnded;
        JoinConfirmation? active;
        lock (_gate) { active = _active; _active = null; }
        active?.Abort();
        await Task.CompletedTask;
    }
}
