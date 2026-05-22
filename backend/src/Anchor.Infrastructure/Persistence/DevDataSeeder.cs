using Anchor.Domain.Classes;
using Anchor.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Anchor.Infrastructure.Persistence;

public static class DevDataSeeder
{
    private static readonly Guid TeacherEntraOid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentEntraOid = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static async Task SeedAsync(AnchorDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken)) return;

        var teacher = new User
        {
            EntraOid = TeacherEntraOid,
            DisplayName = "Dev Teacher",
            Role = UserRole.Teacher,
        };
        var student = new User
        {
            EntraOid = StudentEntraOid,
            DisplayName = "Dev Student",
            Role = UserRole.Student,
        };

        var klas = new Class
        {
            Name = "3A",
            SchoolYear = "2025-2026",
        };

        var membership = new ClassMembership
        {
            ClassId = klas.Id,
            UserId = student.Id,
            Role = ClassMembershipRole.Member,
        };

        db.Users.AddRange(teacher, student);
        db.Classes.Add(klas);
        db.ClassMemberships.Add(membership);

        await db.SaveChangesAsync(cancellationToken);
    }
}
