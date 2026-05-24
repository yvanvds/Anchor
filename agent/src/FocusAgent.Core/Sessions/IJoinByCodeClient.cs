namespace FocusAgent.Core.Sessions;

/// <summary>
/// Outcome categories for a manual join-by-code attempt. One per response-code
/// branch the backend distinguishes (404 / 409 / 410 / 429 / 200 / network),
/// so the agent UI can show a specific message without parsing strings.
/// </summary>
public enum JoinByCodeStatus
{
    Success,
    /// <summary>Code did not match an active session.</summary>
    NotFound,
    /// <summary>Session has ended or is past the freshness window.</summary>
    Expired,
    /// <summary>Caller is already an active participant of another session.</summary>
    AlreadyInSession,
    /// <summary>Too many failed attempts in a short window.</summary>
    RateLimited,
    /// <summary>Caller is not authenticated.</summary>
    Unauthorized,
    /// <summary>Network / transport failure, or unexpected backend response.</summary>
    NetworkError,
}

public sealed record JoinByCodeOutcome(JoinByCodeStatus Status, string Message);

/// <summary>
/// Talks to the backend's <c>POST /sessions/join-by-code</c> endpoint. Lives
/// in Core (abstracted away from HttpClient) so the dialog orchestrator can
/// be exercised without the full WinUI host on the stack — the App project
/// provides the real HTTP implementation.
/// </summary>
public interface IJoinByCodeClient
{
    Task<JoinByCodeOutcome> JoinAsync(string code, CancellationToken ct = default);
}
