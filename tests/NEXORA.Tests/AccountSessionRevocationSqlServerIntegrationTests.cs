using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NEXORA.Application.Email;
using NEXORA.Application.Identity;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;
using NEXORA.Web.Email;
using NEXORA.Web.Identity;

namespace NEXORA.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AccountSessionRevocationSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task AccountLock_RejectsExistingCookieOnTheVeryNextProtectedRequest()
    {
        var connectionString = await AccountAdministrationSqlServerTestSupport.GetVerifiedQaConnectionStringAsync();
        var email = AccountAdministrationSqlServerTestSupport.NewQaEmail("cookie-lock");
        await using var app = await StartQaApplicationAsync(connectionString);
        try
        {
            var userId = await CreateStaffFixtureAsync(app.Services, email, RoleNames.Sales);
            using var client = CreateClient(app);
            using var signIn = await client.GetAsync($"/test/signin/{Uri.EscapeDataString(email)}");
            Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
            using var before = await client.GetAsync("/test/sales");
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                Assert.Equal(AccountOperationStatus.Success,
                    (await service.LockUserAsync(userId, 900101)).Status);
            }

            using var after = await client.GetAsync("/test/sales");
            Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
            Assert.Contains("/account/login", after.Headers.Location?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await DeleteFixtureAsync(app.Services, email);
        }
    }

    [SqlServerFact]
    public async Task RoleChange_RejectsOldRoleCookieOnTheVeryNextProtectedRequest()
    {
        var connectionString = await AccountAdministrationSqlServerTestSupport.GetVerifiedQaConnectionStringAsync();
        var email = AccountAdministrationSqlServerTestSupport.NewQaEmail("cookie-role");
        await using var app = await StartQaApplicationAsync(connectionString);
        try
        {
            var userId = await CreateStaffFixtureAsync(app.Services, email, RoleNames.Sales);
            using var client = CreateClient(app);
            using var signIn = await client.GetAsync($"/test/signin/{Uri.EscapeDataString(email)}");
            Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
            using var before = await client.GetAsync("/test/sales");
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
                Assert.Equal(AccountOperationStatus.Success,
                    (await service.ChangeStaffRoleAsync(userId, RoleNames.Warehouse, 900102)).Status);
            }

            using var staleSales = await client.GetAsync("/test/sales");
            using var staleWarehouse = await client.GetAsync("/test/warehouse");
            Assert.Equal(HttpStatusCode.Redirect, staleSales.StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, staleWarehouse.StatusCode);
        }
        finally
        {
            await DeleteFixtureAsync(app.Services, email);
        }
    }

    private static async Task<WebApplication> StartQaApplicationAsync(string connectionString)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AccountSessionRevocationSqlServerIntegrationTests).Assembly.GetName().Name,
            EnvironmentName = "QA"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddNexoraInfrastructure(connectionString, "QA");
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<InMemoryEmailMailbox>();
        builder.Services.AddScoped<IEmailSender, DevelopmentEmailSender>();
        builder.Services.Configure<StaffInvitationOptions>(options => options.PublicBaseUrl = "https://nexora.test");
        builder.Services.AddIdentity<AppUser, AppRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredUniqueChars = 4;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/login";
            options.Cookie.Name = "NEXORA.Phase61.QA.Test";
            options.Cookie.HttpOnly = true;
        });
        builder.Services.AddAuthoritativeAccountCookieValidation();

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/test/signin/{email}", async (string email,
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn) =>
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null) return Results.NotFound();
            await signIn.SignInAsync(user, isPersistent: false);
            return Results.Ok();
        }).AllowAnonymous();
        app.MapGet("/test/sales", () => Results.Ok("sales"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Sales });
        app.MapGet("/test/warehouse", () => Results.Ok("warehouse"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Warehouse });

        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app) => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        CookieContainer = new System.Net.CookieContainer(),
        UseCookies = true
    })
    {
        BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
    };

    private static async Task<int> CreateStaffFixtureAsync(IServiceProvider services, string email, string role)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            FullName = "NEXORA Phase 6.1 cookie fixture",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var create = await manager.CreateAsync(user, AccountAdministrationSqlServerTestSupport.NewPassword());
        Assert.True(create.Succeeded, string.Join(",", create.Errors.Select(error => error.Code)));
        var assign = await manager.AddToRoleAsync(user, role);
        Assert.True(assign.Succeeded, string.Join(",", assign.Errors.Select(error => error.Code)));
        return user.Id;
    }

    private static async Task DeleteFixtureAsync(IServiceProvider services, string email)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await manager.FindByEmailAsync(email);
        if (user is not null)
        {
            var deleted = await manager.DeleteAsync(user);
            Assert.True(deleted.Succeeded, string.Join(",", deleted.Errors.Select(error => error.Code)));
        }
    }
}
