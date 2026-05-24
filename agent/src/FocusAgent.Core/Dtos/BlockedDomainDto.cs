namespace FocusAgent.Core.Dtos;

/// <summary>
/// One entry in the session's loose-mode blocklist. Wire-shape twin of the
/// backend's <c>BlockedDomainDto</c> (#76). The agent itself never enforces
/// against this list — URL-level filtering lives in the Edge extension — but
/// the DTO ships in <see cref="SessionStartedPayload"/> so rejoin
/// rehydration carries the same shape the extension sees on a fresh start.
/// </summary>
public sealed record BlockedDomainDto(string MatchType, string Value);
