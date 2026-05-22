using Anchor.Domain.Bundles;

namespace Anchor.Domain.Sessions;

public sealed class SessionBundle
{
    public required Guid SessionId { get; init; }
    public required Guid BundleId { get; init; }

    public Session? Session { get; init; }
    public Bundle? Bundle { get; init; }
}
