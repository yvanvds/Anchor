using FocusAgent.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace FocusAgent.App.Sessions;

/// <summary>
/// Dev-only <see cref="ISessionUiHost"/> that auto-confirms every
/// join-confirmation instead of rendering the WinUI toast (#93). Registered
/// only when the agent is launched with <c>--auto-join</c>, so a headless
/// verify run actually joins the session — which is the prerequisite for
/// receiving mid-session <c>SessionBundlesUpdated</c> pushes (those target
/// active participants only). Never wired up in a production launch.
/// </summary>
public sealed class AutoConfirmSessionUiHost : ISessionUiHost
{
    private readonly ILogger<AutoConfirmSessionUiHost> _log;

    public AutoConfirmSessionUiHost(ILogger<AutoConfirmSessionUiHost> log)
    {
        _log = log;
    }

    public Task<JoinDecision> ShowJoinConfirmationAsync(JoinConfirmation confirmation, CancellationToken ct = default)
    {
        _log.LogWarning(
            "--auto-join: auto-confirming session {SessionId} without the toast (DEV ONLY).",
            confirmation.Payload.SessionId);
        return Task.FromResult(JoinDecision.Confirmed);
    }

    public void DismissJoinConfirmation()
    {
        // Nothing to dismiss — there is no window.
    }
}
