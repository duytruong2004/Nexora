using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NEXORA.Application.Identity;
using NEXORA.Web.Areas.Admin.Controllers;

namespace NEXORA.Tests;

public sealed class AccountAdministrationHttpIntegrationTests
{
    [Fact]
    public async Task UsersArea_IsAdminOnly_AndCreateFormNeverOffersAdminRole()
    {
        var service = new FakeAccountAdministrationService();
        var builder = CreateBuilder(service);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
            };

            foreach (var role in new[] { "Customer", "Sales", "Warehouse" })
            {
                using var forbidden = await client.GetAsync($"/Admin/Users/Index?testRole={role}");
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            }

            using var anonymous = await client.GetAsync("/Admin/Users/Index?testAnonymous=true");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            using var index = await client.GetAsync("/Admin/Users/Index?testRole=Admin");
            var indexHtml = await index.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, index.StatusCode);
            Assert.Contains("Tài khoản", indexHtml, StringComparison.Ordinal);
            Assert.Contains("NEXORA QA Admin", indexHtml, StringComparison.Ordinal);

            using var create = await client.GetAsync("/Admin/Users/CreateStaff?testRole=Admin");
            var html = await create.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            Assert.Contains("value=\"Sales\"", html, StringComparison.Ordinal);
            Assert.Contains("value=\"Warehouse\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<option value=\"Admin\"", html, StringComparison.Ordinal);

            using var noAntiforgery = await client.PostAsync(
                "/Admin/Users/CreateStaff?testRole=Admin",
                new FormUrlEncodedContent([new KeyValuePair<string, string>("Role", "Admin")]));
            Assert.Equal(HttpStatusCode.BadRequest, noAntiforgery.StatusCode);
            Assert.Equal(0, service.CreateCalls);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task AdminTamperedCreateRole_IsRejectedByServerValidationWithAntiforgery()
    {
        var service = new FakeAccountAdministrationService();
        var builder = CreateBuilder(service);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
            };
            using var get = await client.GetAsync("/Admin/Users/CreateStaff?testRole=Admin");
            var html = await get.Content.ReadAsStringAsync();
            var token = Regex.Match(html,
                "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(token));
            var cookie = get.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.Select(value => value.Split(';', 2)[0]).ToArray()
                : [];

            using var request = new HttpRequestMessage(HttpMethod.Post,
                "/Admin/Users/CreateStaff?testRole=Admin")
            {
                Content = new FormUrlEncodedContent([
                    new("FullName", "NEXORA QA tampered role"),
                    new("Email", "qa.tampered-role@nexora.test"),
                    new("Role", "Admin"),
                    new("__RequestVerificationToken", token)])
            };
            if (cookie.Length > 0)
            {
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookie));
            }

            using var response = await client.SendAsync(request);
            var responseHtml = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("id=\"Role\"", responseHtml, StringComparison.Ordinal);
            Assert.Equal(0, service.CreateCalls);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task StaffActivationKeepsInvitationSecretsOutOfRequestUrlAndValidatesWithAntiforgery()
    {
        var service = new FakeAccountAdministrationService();
        var builder = CreateBuilder(service);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

        await app.StartAsync();
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new() };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
            };

            using var get = await client.GetAsync("/account/ActivateStaff");
            var html = await get.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Contains("staff-activation.js", html, StringComparison.Ordinal);
            Assert.DoesNotContain("token=", get.RequestMessage?.RequestUri?.Query ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            var antiforgeryToken = Regex.Match(html,
                "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(antiforgeryToken));

            using var noToken = await client.PostAsync("/account/ValidateStaffInvitation",
                new FormUrlEncodedContent([new("Email", "qa.invitation@nexora.test"), new("Token", "example") ]));
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
            Assert.Equal(0, service.InvitationValidationCalls);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/account/ValidateStaffInvitation")
            {
                Content = new FormUrlEncodedContent([
                    new("Email", "qa.invitation@nexora.test"),
                    new("Token", "example")])
            };
            request.Headers.TryAddWithoutValidation("RequestVerificationToken", antiforgeryToken);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("false", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, service.InvitationValidationCalls);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static WebApplicationBuilder CreateBuilder(FakeAccountAdministrationService service)
    {
        var root = FindWebProjectRoot();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(UsersController).Assembly.GetName().Name,
            ContentRootPath = root,
            WebRootPath = Path.Combine(root, "wwwroot"),
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication("Phase61Test")
            .AddScheme<AuthenticationSchemeOptions, Phase61TestAuthenticationHandler>("Phase61Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAccountAdministrationService>(service);
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(UsersController).Assembly);
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

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "NEXORA.Web", "NEXORA.Web.csproj");
            if (File.Exists(candidate)) return Path.Combine(current.FullName, "src", "NEXORA.Web");
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/NEXORA.Web from the test output directory.");
    }

    private sealed class Phase61TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (string.Equals(Request.Query["testAnonymous"], "true", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var role = Request.Query["testRole"].FirstOrDefault() ?? "Admin";
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "700001"),
                new Claim(ClaimTypes.Name, "NEXORA QA Admin"),
                new Claim(ClaimTypes.Role, role)
            ], Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class FakeAccountAdministrationService : IAccountAdministrationService
    {
        public int CreateCalls { get; private set; }
        public int InvitationValidationCalls { get; private set; }

        public Task<ManagedAccountPage> SearchUsersAsync(AccountSearchFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ManagedAccountPage(
                [new ManagedAccountRow(1, "NEXORA QA Admin", "admin@nexora.test", ["Admin"], true, true, true,
                    DateTime.UtcNow, AccountLifecycleState.Active)], 1, 1, 25));

        public Task<ManagedAccountDetails?> GetUserDetailsAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedAccountDetails?>(null);

        public Task<AccountOperationResult> CreateStaffAsync(CreateStaffInput input, int actorUserId, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(new AccountOperationResult(AccountOperationStatus.Success, 1));
        }

        public Task<AccountOperationResult> LockUserAsync(int targetUserId, int actorUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountOperationResult(AccountOperationStatus.Success, targetUserId));

        public Task<AccountOperationResult> UnlockUserAsync(int targetUserId, int actorUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountOperationResult(AccountOperationStatus.Success, targetUserId));

        public Task<AccountOperationResult> ChangeStaffRoleAsync(int targetUserId, string newRole, int actorUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountOperationResult(AccountOperationStatus.Success, targetUserId));

        public Task<AccountOperationResult> ResendInvitationAsync(int targetUserId, int actorUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountOperationResult(AccountOperationStatus.Success, targetUserId));

        public Task<bool> IsInvitationValidAsync(string email, string token, CancellationToken cancellationToken = default)
        {
            InvitationValidationCalls++;
            return Task.FromResult(false);
        }

        public Task<AccountOperationResult> ActivateStaffAsync(string email, string token, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountOperationResult(AccountOperationStatus.InvalidInvitation));
    }
}
