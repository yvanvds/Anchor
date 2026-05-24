// Pure host-match logic for the session allowlist / loose-mode blocklist.
// No DOM, no chrome APIs — runnable in vanilla Node so it can be unit-tested
// without a browser harness. Mirrors the wire shape produced by the backend's
// SessionAllowlistExpander (#70, #76): each entry carries a MatchType string
// and a value, and the MatchType vocabulary tracks BundleEntryMatchType /
// AllowedDomainMatchTypes so the agent and the extension speak the same
// dialect.

/**
 * The match-type values the backend emits in AllowedDomainDto.MatchType /
 * BlockedDomainDto.MatchType. Kept as a string union (not an enum) because
 * the wire format is strings; an enum would force a translation layer on
 * every payload.
 */
export type DomainMatchType = 'Exact' | 'Wildcard' | 'Suffix';

export interface AllowedDomain {
  matchType: DomainMatchType;
  value: string;
}

export interface BlockedDomain {
  matchType: DomainMatchType;
  value: string;
}

/**
 * Returns true if the URL's hostname matches any rule in the list. This is
 * the shared primitive both modes' decision functions wrap — keeping the
 * match logic in one place so allow- and block-lists can't drift in subtle
 * ways (e.g. one being case-sensitive and the other not).
 *
 * Non-http(s) URLs (chrome:, edge:, file:, about:, javascript:, data:, ...)
 * never match: they're treated as not-a-navigation by the upstream callers
 * (block-pageworthy navigations only happen on http/s), so they evaluate to
 * "doesn't match anything" and the caller's allow/block default takes over.
 */
export function hostMatchesAny(url: string, rules: ReadonlyArray<{ matchType: DomainMatchType; value: string }>): boolean {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return false;
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    return false;
  }
  const hostname = parsed.hostname.toLowerCase();
  if (hostname.length === 0) return false;

  for (const rule of rules) {
    if (matches(hostname, rule)) return true;
  }
  return false;
}

/**
 * Strict-mode decision: allow only if the host matches an entry in the
 * allowlist.
 *
 * Rules:
 * - `Exact`    — case-insensitive hostname equality.
 * - `Wildcard` — the rule value is expected to be `*.<suffix>`; matches the
 *                literal `<suffix>` AND any subdomain `*.<suffix>`. We accept
 *                bare `<suffix>` too (treated the same) so a misconfigured
 *                catalogue entry can't silently fail open or closed.
 * - `Suffix`   — same semantics as Wildcard but the value may or may not
 *                start with `*.`; either form matches `<suffix>` and any
 *                subdomain.
 *
 * Non-http(s) URLs (chrome:, edge:, file:, about:, javascript:, data:, ...) are
 * allowed: they never represent navigable user content the extension should
 * police, and trying to block them would only break the browser chrome itself.
 *
 * The empty allowlist case is handled by the caller — when there's no active
 * session, this function isn't called at all. When a session IS active and
 * carries an empty allowlist, the baseline (#70) has already merged in the
 * always-allowed domains server-side, so the empty case here means "block".
 */
export function isUrlAllowed(url: string, rules: ReadonlyArray<AllowedDomain>): boolean {
  if (isExemptScheme(url)) return true;
  return hostMatchesAny(url, rules);
}

/**
 * Loose-mode decision (#76): block iff the host matches the blocklist AND is
 * NOT in the baseline allowlist. Baseline always-allow (auth domains, our
 * backend, fonts) takes precedence so login flows never accidentally trip
 * the social/video/gaming filter — e.g. login.microsoftonline.com must keep
 * working even if some future blocklist entry overlaps.
 *
 * Returns true when the URL should be blocked, false when it should pass
 * through. Non-http(s) URLs always pass through (same rationale as
 * isUrlAllowed).
 */
export function isUrlBlockedByLoose(
  url: string,
  baselineAllow: ReadonlyArray<AllowedDomain>,
  blocked: ReadonlyArray<BlockedDomain>,
): boolean {
  if (isExemptScheme(url)) return false;
  if (hostMatchesAny(url, baselineAllow)) return false;
  return hostMatchesAny(url, blocked);
}

function isExemptScheme(url: string): boolean {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return true;
  }
  return parsed.protocol !== 'http:' && parsed.protocol !== 'https:';
}

function matches(hostname: string, rule: { matchType: DomainMatchType; value: string }): boolean {
  const raw = rule.value?.trim().toLowerCase();
  if (!raw) return false;

  switch (rule.matchType) {
    case 'Exact':
      return hostname === raw;

    case 'Wildcard':
    case 'Suffix': {
      // Accept the canonical `*.foo.com` form, but also a bare `foo.com`
      // (catalogue authors do both in practice; the backend doesn't enforce
      // the leading `*.`). Both should match `foo.com` AND any subdomain.
      const suffix = raw.startsWith('*.') ? raw.slice(2) : raw;
      if (suffix.length === 0) return false;
      if (hostname === suffix) return true;
      return hostname.endsWith('.' + suffix);
    }

    default:
      return false;
  }
}
