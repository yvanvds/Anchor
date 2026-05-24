using FocusAgent.Core.Dtos;

namespace FocusAgent.Core.Sessions;

/// <summary>
/// Talks to the backend's <c>GET /sessions/rejoinable</c> endpoint (#54) to
/// discover sessions the student is still an active participant of after an
/// agent restart. Abstracted as an interface so the Core-side rehydration
/// service can be unit-tested without an HTTP stack — the App project
/// provides the real HttpClient-backed implementation.
/// </summary>
public interface ISessionRehydrationClient
{
    Task<IReadOnlyList<SessionStartedPayload>> GetRejoinableSessionsAsync(CancellationToken ct = default);
}
