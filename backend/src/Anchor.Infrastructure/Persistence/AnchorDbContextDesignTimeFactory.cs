using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Anchor.Infrastructure.Persistence;

internal sealed class AnchorDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AnchorDbContext>
{
    private const string LocalDbFallback =
        "Server=(localdb)\\mssqllocaldb;Database=Anchor.Design;Trusted_Connection=True;";

    public AnchorDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? LocalDbFallback;

        var options = new DbContextOptionsBuilder<AnchorDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(AnchorDbContext).Assembly.FullName))
            .Options;

        return new AnchorDbContext(options);
    }
}
