using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NEXORA.Application.Email;
using NEXORA.Application.Identity;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;
using NEXORA.Web.Controllers;
using NEXORA.Web.Identity;

namespace NEXORA.Tests;

public sealed class AccountLoginPolicyHttpTests
{
    private const string Email = "multi-role-login@nexora.test";
    private static readonly string Password = $"Nexora-Test-{Guid.NewGuid():N}!Aa1";

    [Fact]
    public async Task MultiRoleLogin_DoesNotIssueAuthenticationCookie()
    {
        var data = new TestIdentityData(RoleNames.Admin, RoleNames.Customer);
        var builder = CreateBuilder(data);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/test/admin", () => Results.Ok("admin"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });

        await app.StartAsync();
        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = new System.Net.CookieContainer()
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
            };

            using var get = await client.GetAsync("/account/login");
            var html = await get.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var token = Regex.Match(html,
                "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(token));

            using var post = await client.PostAsync("/account/login", new FormUrlEncodedContent(
            [
                new("Email", Email),
                new("Password", Password),
                new("RememberMe", "false"),
                new("__RequestVerificationToken", token)
            ]));
            var responseHtml = await post.Content.ReadAsStringAsync();
            var summaryHtml = Regex.Match(responseHtml,
                "<div class=\"alert alert-danger[^\"]*\" role=\"alert\">(?<message>.*?)</div>",
                RegexOptions.Singleline).Groups["message"].Value;
            var summaryText = WebUtility.HtmlDecode(Regex.Replace(summaryHtml, "<[^>]+>", " ").Trim());

            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            Assert.Contains("Email hoặc mật khẩu không đúng.", summaryText, StringComparison.Ordinal);
            Assert.Equal(1, data.PasswordHashReadCount);
            Assert.Equal(1, data.RoleReadCount);
            Assert.DoesNotContain(post.Headers.TryGetValues("Set-Cookie", out var values)
                ? values
                : [], value => value.Contains("NEXORA.P61.Login", StringComparison.Ordinal));

            using var protectedResponse = await client.GetAsync("/test/admin");
            Assert.Equal(HttpStatusCode.Redirect, protectedResponse.StatusCode);
            Assert.Contains("/account/login", protectedResponse.Headers.Location?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData(RoleNames.Admin, "/test/admin")]
    [InlineData(RoleNames.Customer, "/test/customer")]
    [InlineData(RoleNames.Sales, "/test/sales")]
    [InlineData(RoleNames.Warehouse, "/test/warehouse")]
    public async Task SingleRoleLogin_StillIssuesCookieAndCanAccessItsRoleEndpoint(
        string role,
        string protectedPath)
    {
        var builder = CreateBuilder(new TestIdentityData(role));
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        app.MapGet("/test/customer", () => Results.Ok("customer"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Customer });
        app.MapGet("/test/admin", () => Results.Ok("admin"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });
        app.MapGet("/test/sales", () => Results.Ok("sales"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Sales });
        app.MapGet("/test/warehouse", () => Results.Ok("warehouse"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Warehouse });

        await app.StartAsync();
        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = new System.Net.CookieContainer()
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
            };

            using var get = await client.GetAsync("/account/login");
            var html = await get.Content.ReadAsStringAsync();
            var token = Regex.Match(html,
                "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(token));

            using var post = await client.PostAsync("/account/login", new FormUrlEncodedContent(
            [
                new("Email", Email),
                new("Password", Password),
                new("RememberMe", "false"),
                new("__RequestVerificationToken", token)
            ]));

            var postBody = await post.Content.ReadAsStringAsync();
            var safeDiagnostic = Regex.Replace(postBody, "value=\"[^\"]*\"", "value=\"[redacted]\"");
            Assert.True(post.StatusCode == HttpStatusCode.Redirect,
                safeDiagnostic[..Math.Min(safeDiagnostic.Length, 1200)]);
            Assert.True(post.Headers.TryGetValues("Set-Cookie", out var values));
            Assert.Contains(values!, value => value.Contains("NEXORA.P61.Login", StringComparison.Ordinal));
            using var roleResponse = await client.GetAsync(protectedPath);
            Assert.Equal(HttpStatusCode.OK, roleResponse.StatusCode);

            var roleEndpoints = new[]
            {
                (RoleNames.Admin, "/test/admin"),
                (RoleNames.Customer, "/test/customer"),
                (RoleNames.Sales, "/test/sales"),
                (RoleNames.Warehouse, "/test/warehouse")
            };
            foreach (var (endpointRole, endpointPath) in roleEndpoints.Where(item => item.Item1 != role))
            {
                using var forbiddenResponse = await client.GetAsync(endpointPath);
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task ZeroRoleLogin_DoesNotIssueAuthenticationCookie()
    {
        var data = new TestIdentityData();
        var builder = CreateBuilder(data);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/test/admin", () => Results.Ok("admin"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });

        await app.StartAsync();
        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = new System.Net.CookieContainer()
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
            };

            using var get = await client.GetAsync("/account/login");
            var html = await get.Content.ReadAsStringAsync();
            var token = Regex.Match(html,
                "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(token));

            using var post = await client.PostAsync("/account/login", new FormUrlEncodedContent(
            [
                new("Email", Email),
                new("Password", Password),
                new("RememberMe", "false"),
                new("__RequestVerificationToken", token)
            ]));

            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            Assert.Equal(1, data.PasswordHashReadCount);
            Assert.Equal(1, data.RoleReadCount);
            Assert.DoesNotContain(post.Headers.TryGetValues("Set-Cookie", out var values)
                ? values
                : [], value => value.Contains("NEXORA.P61.Login", StringComparison.Ordinal));

            using var protectedResponse = await client.GetAsync("/test/admin");
            Assert.Equal(HttpStatusCode.Redirect, protectedResponse.StatusCode);
            Assert.Contains("/account/login", protectedResponse.Headers.Location?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task InactiveUserLogin_DoesNotIssueAuthenticationCookie()
    {
        var data = new TestIdentityData(RoleNames.Admin);
        data.User.IsActive = false;
        var builder = CreateBuilder(data);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await app.StartAsync();
        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = new System.Net.CookieContainer()
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
            };

            using var get = await client.GetAsync("/account/login");
            var html = await get.Content.ReadAsStringAsync();
            var token = Regex.Match(html,
                "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
            using var post = await client.PostAsync("/account/login", new FormUrlEncodedContent(
            [
                new("Email", Email),
                new("Password", Password),
                new("RememberMe", "false"),
                new("__RequestVerificationToken", token)
            ]));

            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            Assert.Equal(0, data.PasswordHashReadCount);
            Assert.Equal(0, data.RoleReadCount);
            Assert.DoesNotContain(post.Headers.TryGetValues("Set-Cookie", out var values)
                ? values
                : [], value => value.Contains("NEXORA.P61.Login", StringComparison.Ordinal));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static WebApplicationBuilder CreateBuilder(TestIdentityData data)
    {
        var webRoot = FindWebProjectRoot();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AccountController).Assembly.GetName().Name,
            ContentRootPath = webRoot,
            WebRootPath = Path.Combine(webRoot, "wwwroot"),
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.LoginPath = "/account/login";
                options.AccessDeniedPath = "/account/accessdenied";
                options.Cookie.Name = "NEXORA.P61.Login.Test";
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddIdentityCore<AppUser>(options =>
            {
                options.SignIn.RequireConfirmedEmail = false;
                options.Lockout.AllowedForNewUsers = false;
            })
            .AddRoles<AppRole>()
            .AddUserStore<TestUserStore>()
            .AddRoleStore<TestRoleStore>()
            .AddSignInManager();
        builder.Services.AddSingleton(data);
        builder.Services.AddSingleton<IAccountAuthenticationStateReader>(new TestAccountStateReader(data));
        builder.Services.AddAuthoritativeAccountCookieValidation();
        builder.Services.AddSingleton<IEmailSender, TestEmailSender>();
        builder.Services
            .AddControllersWithViews()
            .AddApplicationPart(typeof(AccountController).Assembly);
        return builder;
    }

    private static string FindWebProjectRoot()
    {
        var configuredRepositoryRoot = Environment.GetEnvironmentVariable("NEXORA_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRepositoryRoot))
        {
            var configuredProjectRoot = Path.Combine(configuredRepositoryRoot, "src", "NEXORA.Web");
            if (File.Exists(Path.Combine(configuredProjectRoot, "NEXORA.Web.csproj")))
            {
                return configuredProjectRoot;
            }
        }

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

    private sealed class TestIdentityData
    {
        private int _passwordHashReadCount;
        private int _roleReadCount;

        public TestIdentityData(params string[] roles)
        {
            User = CreateUser();
            Roles = roles;
            RoleRows = RoleNames.All.Select((role, index) => new AppRole
            {
                Id = index + 1,
                Name = role,
                NormalizedName = role.ToUpperInvariant()
            }).ToArray();
        }

        public AppUser User { get; }
        public string[] Roles { get; }
        public int PasswordHashReadCount => Volatile.Read(ref _passwordHashReadCount);
        public int RoleReadCount => Volatile.Read(ref _roleReadCount);
        public AppRole[] RoleRows { get; }

        public string? ReadPasswordHash()
        {
            Interlocked.Increment(ref _passwordHashReadCount);
            return User.PasswordHash;
        }

        public IList<string> ReadRoles()
        {
            Interlocked.Increment(ref _roleReadCount);
            return Roles;
        }

        private static AppUser CreateUser()
        {
            var user = new AppUser
            {
                Id = 82,
                UserName = Email,
                NormalizedUserName = Email.ToUpperInvariant(),
                Email = Email,
                NormalizedEmail = Email.ToUpperInvariant(),
                FullName = "NEXORA isolated login test",
                IsActive = true,
                SecurityStamp = $"test-stamp-{Guid.NewGuid():N}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, Password);
            return user;
        }
    }

    private sealed class TestUserStore(TestIdentityData data)
        : IUserStore<AppUser>, IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>, IUserRoleStore<AppUser>, IUserSecurityStampStore<AppUser>
    {
        public Task<IdentityResult> CreateAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<AppUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(int.TryParse(userId, out var id) && id == data.User.Id ? data.User : null);
        public Task<AppUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(normalizedUserName, data.User.NormalizedUserName, StringComparison.Ordinal) ? data.User : null);
        public Task<string> GetUserIdAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString());
        public Task<string?> GetUserNameAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(AppUser user, string? userName, CancellationToken cancellationToken) { user.UserName = userName; return Task.CompletedTask; }
        public Task<string?> GetNormalizedUserNameAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(AppUser user, string? normalizedName, CancellationToken cancellationToken) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }
        public Task<string?> GetEmailAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.Email);
        public Task SetEmailAsync(AppUser user, string? email, CancellationToken cancellationToken) { user.Email = email; return Task.CompletedTask; }
        public Task<bool> GetEmailConfirmedAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.EmailConfirmed);
        public Task SetEmailConfirmedAsync(AppUser user, bool confirmed, CancellationToken cancellationToken) { user.EmailConfirmed = confirmed; return Task.CompletedTask; }
        public Task<AppUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(normalizedEmail, data.User.NormalizedEmail, StringComparison.Ordinal) ? data.User : null);
        public Task<string?> GetNormalizedEmailAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedEmail);
        public Task SetNormalizedEmailAsync(AppUser user, string? normalizedEmail, CancellationToken cancellationToken) { user.NormalizedEmail = normalizedEmail; return Task.CompletedTask; }
        public Task SetPasswordHashAsync(AppUser user, string? passwordHash, CancellationToken cancellationToken) { user.PasswordHash = passwordHash; return Task.CompletedTask; }
        public Task<string?> GetPasswordHashAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(data.ReadPasswordHash());
        public Task SetSecurityStampAsync(AppUser user, string? stamp, CancellationToken cancellationToken) { user.SecurityStamp = stamp; return Task.CompletedTask; }
        public Task<string?> GetSecurityStampAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.SecurityStamp);
        public Task<bool> HasPasswordAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash is not null);
        public Task<IList<string>> GetRolesAsync(AppUser user, CancellationToken cancellationToken) => Task.FromResult(data.ReadRoles());
        public Task<bool> IsInRoleAsync(AppUser user, string normalizedRoleName, CancellationToken cancellationToken) => Task.FromResult(data.Roles.Any(role => role.ToUpperInvariant() == normalizedRoleName));
        public Task AddToRoleAsync(AppUser user, string normalizedRoleName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveFromRoleAsync(AppUser user, string normalizedRoleName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IList<AppUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken cancellationToken) => Task.FromResult<IList<AppUser>>([]);
        public void Dispose() { }
    }

    private sealed class TestAccountStateReader(TestIdentityData data) : IAccountAuthenticationStateReader
    {
        public Task<AccountAuthenticationState?> ReadAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountAuthenticationState?>(userId == data.User.Id
                ? new AccountAuthenticationState(data.User.IsActive, data.User.SecurityStamp, data.Roles)
                : null);
    }

    private sealed class TestRoleStore(TestIdentityData data) : IRoleStore<AppRole>, IRoleClaimStore<AppRole>
    {
        public Task<IdentityResult> CreateAsync(AppRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(AppRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(AppRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<AppRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken) =>
            Task.FromResult(data.RoleRows.FirstOrDefault(role => role.Id.ToString() == roleId));
        public Task<AppRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) =>
            Task.FromResult(data.RoleRows.FirstOrDefault(role => role.NormalizedName == normalizedRoleName));
        public Task<string> GetRoleIdAsync(AppRole role, CancellationToken cancellationToken) => Task.FromResult(role.Id.ToString());
        public Task<string?> GetRoleNameAsync(AppRole role, CancellationToken cancellationToken) => Task.FromResult(role.Name);
        public Task SetRoleNameAsync(AppRole role, string? roleName, CancellationToken cancellationToken) { role.Name = roleName; return Task.CompletedTask; }
        public Task<string?> GetNormalizedRoleNameAsync(AppRole role, CancellationToken cancellationToken) => Task.FromResult(role.NormalizedName);
        public Task SetNormalizedRoleNameAsync(AppRole role, string? normalizedName, CancellationToken cancellationToken) { role.NormalizedName = normalizedName; return Task.CompletedTask; }
        public Task<IList<Claim>> GetClaimsAsync(AppRole role, CancellationToken cancellationToken) => Task.FromResult<IList<Claim>>([]);
        public Task AddClaimAsync(AppRole role, Claim claim, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveClaimAsync(AppRole role, Claim claim, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class TestEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
