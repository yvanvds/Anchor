using Anchor.Domain.Users;

namespace Anchor.Domain.Sessions;

public sealed class SessionParticipant
{
    public required Guid SessionId { get; init; }
    public required Guid UserId { get; init; }
    public DateTimeOffset? JoinedAt { get; set; }
    public DateTimeOffset? DeclinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }

    public Session? Session { get; init; }
    public User? User { get; init; }
}
