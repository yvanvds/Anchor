namespace FocusAgent.Core.Dtos;

/// <summary>
/// Wire-shape twin of the backend's <c>SessionBundlesUpdatedPayload</c> (#93).
/// Pushed to the student's user group when the teacher changes the session's
/// bundles mid-session. Carries the full recomputed allowlist — consumers
/// replace, not merge. The agent only enforces <see cref="Apps"/>;
/// <see cref="Domains"/> is carried for the co-located extension.
/// </summary>
public sealed record SessionBundlesUpdatedPayload(
    Guid SessionId,
    IReadOnlyList<AllowedAppDto> Apps,
    IReadOnlyList<AllowedDomainDto> Domains);
