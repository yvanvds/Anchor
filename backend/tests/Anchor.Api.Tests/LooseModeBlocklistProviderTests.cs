using Anchor.Api.Sessions;

namespace Anchor.Api.Tests;

/// <summary>
/// Unit tests for the loose-mode blocklist loader (#76). The provider reads
/// a category-keyed JSON document at startup and flattens it into a list of
/// <see cref="Anchor.Api.Realtime.BlockedDomainDto"/>. These tests write
/// temporary JSON files rather than depending on the shipped catalogue so
/// the assertions don't break every time we add or remove a domain.
/// </summary>
public sealed class LooseModeBlocklistProviderTests : IDisposable
{
    private readonly string _tempDir;

    public LooseModeBlocklistProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "anchor-blocklist-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Flattens_categories_into_suffix_entries()
    {
        var path = WriteJson("""
            {
              "social": ["facebook.com", "tiktok.com"],
              "video": ["youtube.com"],
              "gaming": ["roblox.com"]
            }
            """);

        var provider = new LooseModeBlocklistProvider(path);
        var list = provider.GetBlocklist();

        Assert.Equal(4, list.Count);
        Assert.All(list, entry => Assert.Equal("Suffix", entry.MatchType));
        Assert.Contains(list, e => e.Value == "facebook.com");
        Assert.Contains(list, e => e.Value == "tiktok.com");
        Assert.Contains(list, e => e.Value == "youtube.com");
        Assert.Contains(list, e => e.Value == "roblox.com");
    }

    [Fact]
    public void Skips_blank_and_whitespace_entries()
    {
        var path = WriteJson("""
            {
              "social": ["facebook.com", "", "   "],
              "video": []
            }
            """);

        var provider = new LooseModeBlocklistProvider(path);
        var list = provider.GetBlocklist();

        Assert.Single(list);
        Assert.Equal("facebook.com", list[0].Value);
    }

    [Fact]
    public void Dedupes_across_categories()
    {
        // The category headers are organisational; if "twitch.tv" appears in
        // both video and gaming we should only emit one entry. The host-match
        // primitive doesn't care which bucket it came from.
        var path = WriteJson("""
            {
              "video": ["twitch.tv"],
              "gaming": ["twitch.tv", "TWITCH.TV"]
            }
            """);

        var provider = new LooseModeBlocklistProvider(path);
        var list = provider.GetBlocklist();

        Assert.Single(list, e => e.Value.Equals("twitch.tv", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_file_throws_at_construction()
    {
        var path = Path.Combine(_tempDir, "does-not-exist.json");

        // Loud failure is the design: a silently empty blocklist would turn
        // loose mode into a no-op without anyone noticing.
        Assert.Throws<FileNotFoundException>(() => new LooseModeBlocklistProvider(path));
    }

    [Fact]
    public void Shipped_catalogue_loads_and_contains_seed_entries()
    {
        // The seed entries listed in #76 must actually be present in the
        // checked-in JSON — otherwise the loose-mode acceptance criterion
        // ("blocks roblox.com, allows wikipedia.org") regresses silently.
        var shippedPath = LocateShippedCatalogue();
        var provider = new LooseModeBlocklistProvider(shippedPath);
        var list = provider.GetBlocklist();

        foreach (var seed in new[] { "facebook.com", "tiktok.com", "youtube.com", "roblox.com" })
        {
            Assert.Contains(list, e => e.Value.Equals(seed, StringComparison.OrdinalIgnoreCase));
        }
    }

    private string WriteJson(string content)
    {
        var path = Path.Combine(_tempDir, $"blocklist-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static string LocateShippedCatalogue()
    {
        // Walk up from the test bin directory to the repo root and into the
        // API project's Sessions folder. Avoids depending on MSBuild copying
        // the JSON to the test output directory.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "Anchor.Api", "Sessions", LooseModeBlocklistProvider.BlocklistResourceName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate shipped LooseModeBlocklist.json relative to test base directory.");
    }
}
