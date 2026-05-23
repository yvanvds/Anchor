using System.Collections.Immutable;

namespace FocusAgent.Core.Focus;

/// <summary>
/// Decides whether a foregrounded app is allowed during a session. Combines
/// a built-in baseline (Edge, explorer, the agent's own process) with the
/// teacher-supplied rules so the watcher can never fight Windows itself or
/// the browser whose extension does URL filtering.
/// </summary>
public sealed class AllowlistMatcher
{
    private static readonly ImmutableArray<AllowedAppRule> BaselineRules = ImmutableArray.Create(
        new AllowedAppRule { MatchKind = AllowedAppMatchKind.ProcessName, Value = "msedge" },
        new AllowedAppRule { MatchKind = AllowedAppMatchKind.ProcessName, Value = "explorer" });

    private readonly ImmutableArray<AllowedAppRule> _rules;
    private readonly string? _ownProcessName;

    public AllowlistMatcher(IEnumerable<AllowedAppRule> rules, string? ownProcessName = null)
    {
        _rules = BaselineRules
            .Concat(rules ?? Array.Empty<AllowedAppRule>())
            .Where(r => !string.IsNullOrWhiteSpace(r.Value))
            .ToImmutableArray();
        _ownProcessName = string.IsNullOrWhiteSpace(ownProcessName)
            ? null
            : NormalizeProcessName(ownProcessName);
    }

    public bool IsAllowed(AppInfo app)
    {
        var processName = NormalizeProcessName(app.ProcessName);

        if (_ownProcessName is not null && processName.Equals(_ownProcessName, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var rule in _rules)
        {
            if (Matches(rule, app, processName))
                return true;
        }
        return false;
    }

    private static bool Matches(AllowedAppRule rule, AppInfo app, string normalizedProcessName) => rule.MatchKind switch
    {
        AllowedAppMatchKind.ProcessName =>
            normalizedProcessName.Equals(NormalizeProcessName(rule.Value), StringComparison.OrdinalIgnoreCase),

        AllowedAppMatchKind.ExecutablePath =>
            !string.IsNullOrEmpty(app.ExecutablePath) &&
            string.Equals(
                Path.GetFullPath(app.ExecutablePath),
                Path.GetFullPath(rule.Value),
                StringComparison.OrdinalIgnoreCase),

        AllowedAppMatchKind.Publisher =>
            !string.IsNullOrEmpty(app.SignedPublisher) &&
            string.Equals(app.SignedPublisher, rule.Value, StringComparison.OrdinalIgnoreCase),

        _ => false,
    };

    private static string NormalizeProcessName(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];
        return trimmed;
    }
}
