namespace Anchor.Domain.Classes;

public sealed class Class
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string SchoolYear { get; set; }

    public ICollection<ClassMembership> Memberships { get; init; } = new List<ClassMembership>();
}
