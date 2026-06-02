import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mergeDomainsInto, replaceDomains, setActiveSession } from './session-state';
import type { ActiveSessionState, AllowedDomainDto } from './types';

// Minimal in-memory chrome.storage.session so the storage-backed helpers can be
// exercised without a real extension context.
function installChromeStorageMock(): void {
  const store: Record<string, unknown> = {};
  vi.stubGlobal('chrome', {
    storage: {
      session: {
        get: async (key: string) => ({ [key]: store[key] }),
        set: async (items: Record<string, unknown>) => {
          Object.assign(store, items);
        },
        remove: async (key: string) => {
          delete store[key];
        },
      },
    },
  });
}

const baseState = (domains: AllowedDomainDto[]): ActiveSessionState => ({
  sessionId: 'session-1',
  classId: 'class-1',
  joinCode: '123456',
  startedAt: '2026-05-24T00:00:00Z',
  domains,
});

describe('mergeDomainsInto', () => {
  it('returns the same reference when nothing is added', () => {
    const state = baseState([{ matchType: 'Suffix', value: 'office.com' }]);
    const result = mergeDomainsInto(state, []);
    expect(result).toBe(state);
  });

  it('appends new domains', () => {
    const state = baseState([{ matchType: 'Suffix', value: 'office.com' }]);
    const result = mergeDomainsInto(state, [{ matchType: 'Suffix', value: 'reddit.com' }]);
    expect(result).not.toBe(state);
    expect(result.domains).toHaveLength(2);
    expect(result.domains[1]).toEqual({ matchType: 'Suffix', value: 'reddit.com' });
  });

  it('dedupes case-insensitively on (matchType, value)', () => {
    const state = baseState([{ matchType: 'Suffix', value: 'reddit.com' }]);
    const result = mergeDomainsInto(state, [{ matchType: 'Suffix', value: 'REDDIT.com' }]);
    // No change, so should return the original reference.
    expect(result).toBe(state);
  });

  it('treats Exact vs Suffix as different entries', () => {
    const state = baseState([{ matchType: 'Exact', value: 'reddit.com' }]);
    const result = mergeDomainsInto(state, [{ matchType: 'Suffix', value: 'reddit.com' }]);
    expect(result.domains).toHaveLength(2);
  });

  it('skips empty values', () => {
    const state = baseState([]);
    const result = mergeDomainsInto(state, [
      { matchType: 'Suffix', value: '' },
      { matchType: 'Suffix', value: 'reddit.com' },
    ]);
    expect(result.domains).toEqual([{ matchType: 'Suffix', value: 'reddit.com' }]);
  });
});

describe('replaceDomains', () => {
  beforeEach(() => {
    installChromeStorageMock();
  });

  it('replaces the cached domain set wholesale for the matching session', async () => {
    await setActiveSession(baseState([{ matchType: 'Suffix', value: 'office.com' }]));
    const result = await replaceDomains('session-1', [
      { matchType: 'Exact', value: 'wikipedia.org' },
    ]);
    expect(result?.domains).toEqual([{ matchType: 'Exact', value: 'wikipedia.org' }]);
  });

  it('drops the update when the session id does not match the cache', async () => {
    await setActiveSession(baseState([{ matchType: 'Suffix', value: 'office.com' }]));
    const result = await replaceDomains('other-session', [
      { matchType: 'Exact', value: 'wikipedia.org' },
    ]);
    expect(result).toBeNull();
  });

  it('returns null when there is no active session', async () => {
    const result = await replaceDomains('session-1', []);
    expect(result).toBeNull();
  });
});
