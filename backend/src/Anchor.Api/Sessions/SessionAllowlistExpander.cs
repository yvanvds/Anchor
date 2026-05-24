using Anchor.Api.Realtime;
using Anchor.Domain.Bundles;
using Anchor.Domain.Sessions;
using Anchor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anchor.Api.Sessions;

/// <summary>
/// Turns the set of selected bundle ids + the session's mode into the
/// concrete wire payload the agent and extension will enforce against.
///
/// In <see cref="SessionMode.Strict"/> the expansion is bundles merged with
/// the baseline allowlist (apps + domains), and an empty blocklist (#70).
/// In <see cref="SessionMode.Loose"/> the allowed-domain side is still
/// baseline + any explicitly picked bundles (a teacher who picked loose AND
/// a bundle should not get the bundle silently dropped), and the blocked
/// side carries the curated "known-bad categories" list from
/// <see cref="ILooseModeBlocklistProvider"/> (#76).
///
/// Dedupes on (match-kind, lowercase value) within each list so a bundle
/// that repeats a baseline entry doesn't double-list it.
/// </summary>
public interface ISessionAllowlistExpander
{
    Task<ExpandedAllowlist> ExpandAsync(IReadOnlyCollection<Guid> bundleIds, SessionMode mode, CancellationToken cancellationToken = default);

    Task<ExpandedAllowlist> ExpandForSessionAsync(Guid sessionId, SessionMode mode, CancellationToken cancellationToken = default);
}

public sealed record ExpandedAllowlist(
    IReadOnlyList<AllowedAppDto> Apps,
    IReadOnlyList<AllowedDomainDto> Domains,
    IReadOnlyList<BlockedDomainDto> BlockedDomains);

public sealed class SessionAllowlistExpander : ISessionAllowlistExpander
{
    private readonly AnchorDbContext _db;
    private readonly ILooseModeBlocklistProvider _blocklist;

    public SessionAllowlistExpander(AnchorDbContext db, ILooseModeBlocklistProvider blocklist)
    {
        _db = db;
        _blocklist = blocklist;
    }

    public async Task<ExpandedAllowlist> ExpandAsync(IReadOnlyCollection<Guid> bundleIds, SessionMode mode, CancellationToken cancellationToken = default)
    {
        var entries = bundleIds.Count == 0
            ? Array.Empty<BundleEntry>()
            : await _db.BundleEntries
                .AsNoTracking()
                .Where(e => bundleIds.Contains(e.BundleId))
                .ToArrayAsync(cancellationToken);

        return Merge(entries, mode);
    }

    public async Task<ExpandedAllowlist> ExpandForSessionAsync(Guid sessionId, SessionMode mode, CancellationToken cancellationToken = default)
    {
        var entries = await _db.SessionBundles
            .AsNoTracking()
            .Where(sb => sb.SessionId == sessionId)
            .SelectMany(sb => _db.BundleEntries.Where(e => e.BundleId == sb.BundleId))
            .ToArrayAsync(cancellationToken);

        return Merge(entries, mode);
    }

    private ExpandedAllowlist Merge(IReadOnlyCollection<BundleEntry> entries, SessionMode mode)
    {
        var apps = new List<AllowedAppDto>(SessionAllowlist.BaselineApps);
        var domains = new List<AllowedDomainDto>(SessionAllowlist.BaselineDomains);

        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                case BundleEntryKind.App:
                    var appKind = MapAppMatchKind(entry.MatchType);
                    if (appKind is not null)
                        apps.Add(new AllowedAppDto(appKind, entry.Value));
                    break;

                case BundleEntryKind.Domain:
                    var domainType = MapDomainMatchType(entry.MatchType);
                    if (domainType is not null)
                        domains.Add(new AllowedDomainDto(domainType, entry.Value));
                    break;
            }
        }

        var blocked = mode == SessionMode.Loose
            ? DedupeBlocked(_blocklist.GetBlocklist())
            : Array.Empty<BlockedDomainDto>();

        return new ExpandedAllowlist(
            DedupeApps(apps),
            DedupeDomains(domains),
            blocked);
    }

    private static string? MapAppMatchKind(BundleEntryMatchType matchType) => matchType switch
    {
        BundleEntryMatchType.Exact => AllowedAppMatchKinds.ProcessName,
        BundleEntryMatchType.SignedPublisher => AllowedAppMatchKinds.Publisher,
        // Wildcard/Suffix don't have a sensible mapping for the agent's
        // process/path/publisher model — skipped rather than silently
        // narrowed. Catalogue editor should not produce these combinations.
        _ => null,
    };

    private static string? MapDomainMatchType(BundleEntryMatchType matchType) => matchType switch
    {
        BundleEntryMatchType.Exact => AllowedDomainMatchTypes.Exact,
        BundleEntryMatchType.Wildcard => AllowedDomainMatchTypes.Wildcard,
        BundleEntryMatchType.Suffix => AllowedDomainMatchTypes.Suffix,
        // SignedPublisher is meaningless for a domain — skip.
        _ => null,
    };

    private static IReadOnlyList<AllowedAppDto> DedupeApps(IEnumerable<AllowedAppDto> source)
    {
        var seen = new HashSet<(string, string)>();
        var result = new List<AllowedAppDto>();
        foreach (var dto in source)
        {
            if (string.IsNullOrWhiteSpace(dto.Value)) continue;
            var key = (dto.MatchKind, dto.Value.Trim().ToLowerInvariant());
            if (seen.Add(key))
                result.Add(dto);
        }
        return result;
    }

    private static IReadOnlyList<AllowedDomainDto> DedupeDomains(IEnumerable<AllowedDomainDto> source)
    {
        var seen = new HashSet<(string, string)>();
        var result = new List<AllowedDomainDto>();
        foreach (var dto in source)
        {
            if (string.IsNullOrWhiteSpace(dto.Value)) continue;
            var key = (dto.MatchType, dto.Value.Trim().ToLowerInvariant());
            if (seen.Add(key))
                result.Add(dto);
        }
        return result;
    }

    private static IReadOnlyList<BlockedDomainDto> DedupeBlocked(IEnumerable<BlockedDomainDto> source)
    {
        var seen = new HashSet<(string, string)>();
        var result = new List<BlockedDomainDto>();
        foreach (var dto in source)
        {
            if (string.IsNullOrWhiteSpace(dto.Value)) continue;
            var key = (dto.MatchType, dto.Value.Trim().ToLowerInvariant());
            if (seen.Add(key))
                result.Add(dto);
        }
        return result;
    }
}
