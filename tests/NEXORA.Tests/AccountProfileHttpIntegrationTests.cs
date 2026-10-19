using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NEXORA.Application.Email;
using NEXORA.Application.Commerce;
using NEXORA.Infrastructure.Identity;
using NEXORA.Web.Controllers;

namespace NEXORA.Tests;

public sealed class AccountProfileHttpIntegrationTests
{
    [Fact]
    public async Task Profile_UsesCurrentAuthenticatedUser_IgnoresTamperedId_AndProtectsLogout()
    {
        var webProjectRoot = FindWebProjectRoot();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AccountController).Assembly.GetName().Name,
            ContentRootPath = webProjectRoot,
            WebRootPath = Path.Combine(webProjectRoot, "wwwroot"),
            EnvironmentName = "Development"
        });

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services
            .AddAuthentication("TestProfile")
            .AddScheme<AuthenticationSchemeOptions, TestProfileAuthenticationHandler>("TestProfile", _ => { })
            .AddCookie(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ExternalScheme)
            .AddCookie(IdentityConstants.TwoFactorUserIdScheme);
        builder.Services.AddAuthorization();
        builder.Services
            .AddIdentityCore<AppUser>()
            .AddRoles<AppRole>()
            .AddUserStore<TestUserStore>()
            .AddRoleStore<TestRoleStore>()
            .AddSignInManager();
        builder.Services.AddSingleton<TestIdentityData>();
        builder.Services.AddSingleton<ICartService, TestCartService>();
        builder.Services.AddSingleton<IEmailSender, TestEmailSender>();
        builder.Services
            .AddControllersWithViews()
            .AddApplicationPart(typeof(AccountController).Assembly);

        await using var app = builder.Build();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

        await app.StartAsync();
        try
        {
            var server = app.Services.GetRequiredService<IServer>();
            var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(address)
            };

            using var profileResponse = await client.GetAsync("/account/profile");
            var profileHtml = await profileResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
            Assert.Contains("QA Current Admin Owner", profileHtml, StringComparison.Ordinal);
            Assert.Contains("admin-owner@nexora.test", profileHtml, StringComparison.Ordinal);
            Assert.Contains("Admin", profileHtml, StringComparison.Ordinal);
            Assert.Contains("Customer", profileHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("QA Other Customer Secret", profileHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("other-customer@nexora.test", profileHtml, StringComparison.Ordinal);
            Assert.Matches("(?i)href=\"/account/profile\"", profileHtml);
            Assert.Contains("Tài khoản", profileHtml, StringComparison.Ordinal);
            Assert.Matches("(?i)action=\"/account/logout\"", profileHtml);
            Assert.Matches("(?i)method=\"post\"", profileHtml);
            Assert.Contains("name=\"__RequestVerificationToken\"", profileHtml, StringComparison.Ordinal);

            using var tamperedIdResponse = await client.GetAsync("/account/profile?id=3");
            var tamperedIdHtml = await tamperedIdResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, tamperedIdResponse.StatusCode);
            Assert.Contains("QA Current Admin Owner", tamperedIdHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("QA Other Customer Secret", tamperedIdHtml, StringComparison.Ordinal);

            using var anonymousProfileResponse = await client.GetAsync("/account/profile?testAnonymous=true");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousProfileResponse.StatusCode);

            var antiForgeryToken = Regex.Match(profileHtml,
                "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(antiForgeryToken));
            using var logoutResponse = await client.PostAsync("/account/logout",
                new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", antiForgeryToken)]));
            Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);

            using var anonymousLogoutResponse = await client.PostAsync("/account/logout?testAnonymous=true", content: null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousLogoutResponse.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string FindWebProjectRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                var projectRoot = Path.Combine(current.FullName, "src", "NEXORA.Web");
                if (File.Exists(Path.Combine(projectRoot, "NEXORA.Web.csproj")))
                {
                    return projectRoot;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate src/NEXORA.Web from the test working directory.");
    }

    private sealed class TestProfileAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (string.Equals(Request.Query["testAnonymous"], "true", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "2"),
                new Claim(ClaimTypes.Name, "admin-owner@nexora.test"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Role, "Customer")
            ],
            Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestIdentityData
    {
        public Dictionary<int, AppUser> Users { get; } = new()
        {
            [2] = new AppUser
            {
                Id = 2,
                UserName = "admin-owner@nexora.test",
                NormalizedUserName = "ADMIN-OWNER@NEXORA.TEST",
                Email = "admin-owner@nexora.test",
                FullName = "QA Current Admin Owner",
                IsActive = true,
                CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            [3] = new AppUser
            {
                Id = 3,
                UserName = "other-customer@nexora.test",
                NormalizedUserName = "OTHER-CUSTOMER@NEXORA.TEST",
                Email = "other-customer@nexora.test",
                FullName = "QA Other Customer Secret",
                IsActive = true,
                CreatedAt = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        public Dictionary<int, string[]> RolesByUserId { get; } = new()
        {
            [2] = ["Admin", "Customer"],
            [3] = ["Customer"]
        };
    }

    private sealed class TestUserStore(TestIdentityData data) : IUserStore<AppUser>, IUserRoleStore<AppUser>
    {
        public Task<IdentityResult> CreateAsync(AppUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(AppUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(AppUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<AppUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(int.TryParse(userId, out var id) && data.Users.TryGetValue(id, out var user) ? user : null);

        public Task<AppUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult(data.Users.Values.FirstOrDefault(user => user.NormalizedUserName == normalizedUserName));

        public Task<string> GetUserIdAsync(AppUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public Task<string?> GetUserNameAsync(AppUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetUserNameAsync(AppUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(AppUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(AppUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task<IList<string>> GetRolesAsync(AppUser user, CancellationToken cancellationToken) =>
            Task.FromResult<IList<string>>(data.RolesByUserId[user.Id]);

        public Task<bool> IsInRoleAsync(AppUser user, string normalizedRoleName, CancellationToken cancellationToken) =>
            Task.FromResult(data.RolesByUserId[user.Id].Contains(normalizedRoleName, StringComparer.OrdinalIgnoreCase));

        public Task AddToRoleAsync(AppUser user, string normalizedRoleName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoveFromRoleAsync(AppUser user, string normalizedRoleName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IList<AppUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken cancellationToken) =>
            Task.FromResult<IList<AppUser>>(data.Users.Values
                .Where(user => data.RolesByUserId[user.Id].Contains(normalizedRoleName, StringComparer.OrdinalIgnoreCase))
                .ToArray());

        public void Dispose() { }
    }

    private sealed class TestRoleStore : IRoleStore<AppRole>
    {
        public Task<IdentityResult> CreateAsync(AppRole role, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(AppRole role, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(AppRole role, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<AppRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken) =>
            Task.FromResult<AppRole?>(null);

        public Task<AppRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) =>
            Task.FromResult<AppRole?>(null);

        public Task<string> GetRoleIdAsync(AppRole role, CancellationToken cancellationToken) =>
            Task.FromResult(role.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public Task<string?> GetRoleNameAsync(AppRole role, CancellationToken cancellationToken) =>
            Task.FromResult(role.Name);

        public Task SetRoleNameAsync(AppRole role, string? roleName, CancellationToken cancellationToken)
        {
            role.Name = roleName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedRoleNameAsync(AppRole role, CancellationToken cancellationToken) =>
            Task.FromResult(role.NormalizedName);

        public Task SetNormalizedRoleNameAsync(AppRole role, string? normalizedName, CancellationToken cancellationToken)
        {
            role.NormalizedName = normalizedName;
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private sealed class TestEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
