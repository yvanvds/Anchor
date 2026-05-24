using Anchor.Domain.Classes;
using Anchor.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Anchor.Infrastructure.Persistence;

public static class DevDataSeeder
{
    private static readonly Guid TeacherEntraOid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentEntraOid = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Dev student who is NOT enrolled in any class. Exists so the manual
    /// join-by-code path (#34) can be verified end-to-end: a rostered student
    /// would receive the original SessionStarted push from the class roster
    /// and we'd be unable to tell whether the manual REST call or the roster
    /// got them there.
    /// </summary>
    public static readonly Guid OutsiderStudentEntraOid = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static async Task SeedAsync(AnchorDbContext db, CancellationToken cancellationToken = default)
    {
        // The main seed is one-shot — early-return so we don't churn classes
        // / memberships on every restart. The outsider student is bolted on
        // below idempotently so dev DBs created before #34 still pick it up.
        await EnsureDevOutsiderStudentAsync(db, cancellationToken);
        if (await db.Users.AnyAsync(u => u.EntraOid != OutsiderStudentEntraOid, cancellationToken)) return;

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

        var teacherMembership = new ClassMembership
        {
            ClassId = klas.Id,
            UserId = teacher.Id,
            Role = ClassMembershipRole.Teacher,
        };
        var studentMembership = new ClassMembership
        {
            ClassId = klas.Id,
            UserId = student.Id,
            Role = ClassMembershipRole.Member,
        };

        db.Users.AddRange(teacher, student);
        db.Classes.Add(klas);
        db.ClassMemberships.AddRange(teacherMembership, studentMembership);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Idempotently inserts the outsider student (see <see cref="OutsiderStudentEntraOid"/>)
    /// if not already present. Intentionally not enrolled in any class —
    /// that's what makes them an outsider for the join-by-code verification.
    /// </summary>
    public static async Task EnsureDevOutsiderStudentAsync(AnchorDbContext db, CancellationToken cancellationToken = default)
    {
        var exists = await db.Users.AnyAsync(u => u.EntraOid == OutsiderStudentEntraOid, cancellationToken);
        if (exists) return;

        db.Users.Add(new User
        {
            EntraOid = OutsiderStudentEntraOid,
            DisplayName = "Dev Outsider",
            Role = UserRole.Student,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Dev convenience: make sure any real teacher who signs in ends up with a
    /// Teacher membership in the seeded class so the end-to-end "Start session"
    /// flow works without manual class-data wiring. No-op outside development.
    /// </summary>
    public static async Task EnsureDevTeacherMembershipAsync(
        AnchorDbContext db, Guid userId, CancellationToken cancellationToken = default)
    {
        var seededClass = await db.Classes
            .FirstOrDefaultAsync(c => c.Name == "3A", cancellationToken);
        if (seededClass is null) return;

        var alreadyTeacher = await db.ClassMemberships.AnyAsync(
            m => m.ClassId == seededClass.Id
                 && m.UserId == userId
                 && m.Role == ClassMembershipRole.Teacher,
            cancellationToken);
        if (alreadyTeacher) return;

        db.ClassMemberships.Add(new ClassMembership
        {
            ClassId = seededClass.Id,
            UserId = userId,
            Role = ClassMembershipRole.Teacher,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
