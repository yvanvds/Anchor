namespace Anchor.Domain.Bundles;

public sealed class Bundle
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required int Version { get; set; }

    public ICollection<BundleEntry> Entries { get; init; } = new List<BundleEntry>();
}
