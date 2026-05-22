using Anchor.Domain.Sessions;
using Anchor.Domain.Users;

namespace Anchor.Domain.Events;

public sealed class Event
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid SessionId { get; init; }
    public required Guid UserId { get; init; }
    public required EventKind Kind { get; init; }
    public required string PayloadJson { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }

    public Session? Session { get; init; }
    public User? User { get; init; }
}
