namespace FocusAgent.Core.Focus;

public sealed record AllowedAppRule
{
    public AllowedAppMatchKind MatchKind { get; init; }
    public string Value { get; init; } = "";
}
