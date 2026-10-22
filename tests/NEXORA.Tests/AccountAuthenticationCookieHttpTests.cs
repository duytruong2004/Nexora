using System.Net;
using System.Security.Claims;
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
using Microsoft.Extensions.Options;
using NEXORA.Application.Identity;
using NEXORA.Domain.Security;
using NEXORA.Web.Identity;

namespace NEXORA.Tests;

public sealed class AccountAuthenticationCookieHttpTests
{
    [Fact]
    public async Task MatchingMultiRoleCookieAndPersistedRoles_AreRejected()
    {
        await AssertRejectedAsync(
            [RoleNames.Admin, RoleNames.Customer],
            [RoleNames.Admin, RoleNames.Customer],
            isActive: true,
            "/test/admin");
    }

    [Fact]
    public async Task AdminCookie_IsRejectedAfterPersistedRoleChangesToCustomer()
    {
        await AssertRejectedAsync([RoleNames.Admin], [RoleNames.Customer], true, "/test/admin");
    }

    [Fact]
    public async Task SalesCookie_IsRejectedAfterPersistedRoleChangesToWarehouse()
    {
        await AssertRejectedAsync([RoleNames.Sales], [RoleNames.Warehouse], true, "/test/warehouse");
    }

    [Fact]
    public async Task Cookie_IsRejectedImmediatelyAfterAccountBecomesInactive()
    {
        await AssertRejectedAsync([RoleNames.Admin], [RoleNames.Admin], false, "/test/admin");
    }

    [Fact]
    public async Task ActiveSingleRoleAdminCookie_CanAccessAdminEndpoint()
    {
        await using var app = await StartAsync([RoleNames.Admin], [RoleNames.Admin], true);
        using var client = CreateClient(app);

        using var signIn = await client.GetAsync("/test/signin?role=Admin");
        using var response = await client.GetAsync("/test/admin");

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CustomerCookie_CannotAccessAdminEndpoint()
    {
        await using var app = await StartAsync([RoleNames.Customer], [RoleNames.Customer], true);
        using var client = CreateClient(app);

        using var signIn = await client.GetAsync("/test/signin?role=Customer");
        using var response = await client.GetAsync("/test/admin");

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectedCookieRedirectsToAnonymousLoginWithoutRedirectLoop()
    {
        await using var app = await StartAsync(
            [RoleNames.Admin, RoleNames.Customer],
            [RoleNames.Admin, RoleNames.Customer],
            true);
        using var client = CreateClient(app);

        using var signIn = await client.GetAsync("/test/signin?role=Admin&role=Customer");
        using var protectedResponse = await client.GetAsync("/test/admin");
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, protectedResponse.StatusCode);
        Assert.Contains("/account/login", protectedResponse.Headers.Location?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        using var login = await client.GetAsync(protectedResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    private static async Task AssertRejectedAsync(
        string[] cookieRoles,
        string[] persistedRoles,
        bool isActive,
        string protectedPath)
    {
        await using var app = await StartAsync(cookieRoles, persistedRoles, isActive);
        using var client = CreateClient(app);
        var signInUrl = "/test/signin?" + string.Join("&", cookieRoles.Select(role => $"role={Uri.EscapeDataString(role)}"));

        using var signIn = await client.GetAsync(signInUrl);
        using var response = await client.GetAsync(protectedPath);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<WebApplication> StartAsync(
        string[] cookieRoles,
        string[] persistedRoles,
        bool isActive)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AccountAuthenticationCookieHttpTests).Assembly.GetName().Name,
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<IdentityOptions>(_ => { });
        builder.Services.AddSingleton<IAccountAuthenticationStateReader>(new TestAccountStateReader(
            new AccountAuthenticationState(isActive, "qa-stamp", persistedRoles)));
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
                options.Cookie.Name = "NEXORA.P61.Policy.Test";
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddAuthoritativeAccountCookieValidation();

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/account/login", () => Results.Ok("login"));
        app.MapGet("/test/signin", async (HttpContext context, IOptions<IdentityOptions> identityOptions) =>
        {
            var claims = new List<Claim>
            {
                new(identityOptions.Value.ClaimsIdentity.UserIdClaimType, "82"),
                new(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType, "qa-stamp")
            };
            claims.AddRange(context.Request.Query["role"].Select(role =>
                new Claim(identityOptions.Value.ClaimsIdentity.RoleClaimType, role!)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                IdentityConstants.ApplicationScheme,
                identityOptions.Value.ClaimsIdentity.UserNameClaimType,
                identityOptions.Value.ClaimsIdentity.RoleClaimType));
            await context.SignInAsync(IdentityConstants.ApplicationScheme, principal);
            return Results.Ok();
        }).AllowAnonymous();
        app.MapGet("/test/admin", () => Results.Ok("admin"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });
        app.MapGet("/test/warehouse", () => Results.Ok("warehouse"))
            .RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Warehouse });

        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app) => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = true,
        CookieContainer = new System.Net.CookieContainer()
    })
    {
        BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
    };

    private sealed class TestAccountStateReader(AccountAuthenticationState state)
        : IAccountAuthenticationStateReader
    {
        public Task<AccountAuthenticationState?> ReadAsync(
            int userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountAuthenticationState?>(userId == 82 ? state : null);
    }
}
