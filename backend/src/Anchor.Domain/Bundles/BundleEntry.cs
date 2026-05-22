namespace Anchor.Domain.Bundles;

public sealed class BundleEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid BundleId { get; init; }
    public required BundleEntryKind Kind { get; set; }
    public required string Value { get; set; }
    public required BundleEntryMatchType MatchType { get; set; }

    public Bundle? Bundle { get; init; }
}
