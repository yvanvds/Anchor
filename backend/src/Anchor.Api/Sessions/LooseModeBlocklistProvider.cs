using System.Text.Json;
using Anchor.Api.Realtime;

namespace Anchor.Api.Sessions;

/// <summary>
/// Loads the curated "known-bad categories" domain list shipped at
/// <c>Sessions/LooseModeBlocklist.json</c> and exposes it as a flat
/// <see cref="BlockedDomainDto"/> sequence. Section 7.3 of
/// focus-system-design.md and #76.
///
/// The JSON file is grouped by category (social / video / gaming) for
/// human-editing convenience; categories are flattened on load because the
/// extension's host-match primitive doesn't care which bucket a domain
/// came from. All entries default to <see cref="AllowedDomainMatchTypes.Suffix"/>
/// so a rule like "facebook.com" also catches "m.facebook.com" — students
/// reach social sites through subdomains far more often than the bare host.
///
/// Singleton with no hot-reload: changing the bundled JSON requires a
/// redeploy. Per the issue, iteration on category contents lives in the
/// pilot-phase backlog rather than the runtime.
/// </summary>
public interface ILooseModeBlocklistProvider
{
    IReadOnlyList<BlockedDomainDto> GetBlocklist();
}

public sealed class LooseModeBlocklistProvider : ILooseModeBlocklistProvider
{
    public const string BlocklistResourceName = "LooseModeBlocklist.json";

    private readonly IReadOnlyList<BlockedDomainDto> _blocklist;

    public LooseModeBlocklistProvider(IWebHostEnvironment env)
        : this(ResolveDefaultPath(env))
    {
    }

    // Path-driven constructor exists for tests; production callers go through
    // the IWebHostEnvironment overload.
    public LooseModeBlocklistProvider(string path)
    {
        _blocklist = Load(path);
    }

    public IReadOnlyList<BlockedDomainDto> GetBlocklist() => _blocklist;

    private static string ResolveDefaultPath(IWebHostEnvironment env)
    {
        // ContentRoot is the API project's deployed directory; the JSON is
        // copied to Sessions/ via the .csproj <Content> entry.
        return Path.Combine(env.ContentRootPath, "Sessions", BlocklistResourceName);
    }

    private static IReadOnlyList<BlockedDomainDto> Load(string path)
    {
        if (!File.Exists(path))
        {
            // Missing file = fail fast at startup rather than silently
            // shipping an empty blocklist that would make loose mode a no-op.
            throw new FileNotFoundException(
                $"Loose-mode blocklist not found at '{path}'. Ensure {BlocklistResourceName} is copied to the output directory.",
                path);
        }

        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, JsonOptions);
        if (raw is null)
            return Array.Empty<BlockedDomainDto>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BlockedDomainDto>();
        foreach (var (_, domains) in raw)
        {
            foreach (var domain in domains)
            {
                if (string.IsNullOrWhiteSpace(domain)) continue;
                var value = domain.Trim();
                if (!seen.Add(value)) continue;
                result.Add(new BlockedDomainDto(AllowedDomainMatchTypes.Suffix, value));
            }
        }
        return result;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
}
