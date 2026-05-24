using Anchor.Api.Realtime;
using Anchor.Api.Sessions;

namespace Anchor.Api.Tests;

/// <summary>
/// Test double for <see cref="ILooseModeBlocklistProvider"/>. Returns an
/// empty list by default; tests that exercise loose-mode payload shape set
/// <see cref="Entries"/> before issuing the session-start request.
/// </summary>
public sealed class StubLooseModeBlocklistProvider : ILooseModeBlocklistProvider
{
    public IReadOnlyList<BlockedDomainDto> Entries { get; set; } = Array.Empty<BlockedDomainDto>();

    public IReadOnlyList<BlockedDomainDto> GetBlocklist() => Entries;
}
