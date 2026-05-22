using Anchor.Domain.Users;

namespace Anchor.Domain.Classes;

public sealed class ClassMembership
{
    public required Guid ClassId { get; init; }
    public required Guid UserId { get; init; }
    public required ClassMembershipRole Role { get; set; }

    public Class? Class { get; init; }
    public User? User { get; init; }
}
