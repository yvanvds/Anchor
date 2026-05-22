using Anchor.Api.Tests.FakeAuth;
using Anchor.Domain.Users;
using Anchor.Infrastructure.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anchor.Api.Tests;

public sealed class AnchorApiFactory : WebApplicationFactory<Program>
{
    static AnchorApiFactory()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection",
            "Server=(localdb)\\test;Database=AnchorTest;Trusted_Connection=True;");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(FakeJwtBearerHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, FakeJwtBearerHandler>(
                    FakeJwtBearerHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = FakeJwtBearerHandler.SchemeName;
                options.DefaultChallengeScheme = FakeJwtBearerHandler.SchemeName;
            });

            services.RemoveAll<IUserStore>();
            services.AddSingleton<IUserStore, InMemoryUserStore>();
        });
    }
}
