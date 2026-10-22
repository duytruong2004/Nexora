using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NEXORA.Application.Identity;

namespace NEXORA.Web.Identity;

public static class AuthoritativeAccountCookieValidation
{
    public static IServiceCollection AddAuthoritativeAccountCookieValidation(this IServiceCollection services)
    {
        services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
        {
            var identityValidation = options.Events.OnValidatePrincipal;
            options.Events.OnValidatePrincipal = async context =>
            {
                if (identityValidation is not null)
                {
                    await identityValidation(context);
                }

                await ValidateAsync(context);
            };
        });

        return services;
    }

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var services = context.HttpContext.RequestServices;
        var options = services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("NEXORA.Identity.AccountCookieValidation");
        var userIdValue = principal.FindFirstValue(options.ClaimsIdentity.UserIdClaimType);
        if (!int.TryParse(userIdValue, out var userId))
        {
            await RejectAsync(context);
            return;
        }

        try
        {
            var accountStateReader = services.GetRequiredService<IAccountAuthenticationStateReader>();
            var account = await accountStateReader.ReadAsync(userId, context.HttpContext.RequestAborted);
            if (account is null || !account.IsActive)
            {
                await RejectAsync(context);
                return;
            }

            var cookieStamp = principal.FindFirstValue(options.ClaimsIdentity.SecurityStampClaimType);
            if (string.IsNullOrEmpty(cookieStamp) ||
                !string.Equals(cookieStamp, account.SecurityStamp, StringComparison.Ordinal))
            {
                await RejectAsync(context);
                return;
            }

            var cookieRoles = principal.FindAll(options.ClaimsIdentity.RoleClaimType)
                .Select(claim => claim.Value)
                .ToArray();

            if (!AccountAuthenticationPolicy.HasCurrentSingleRole(
                    account.IsActive,
                    account.Roles,
                    cookieRoles))
            {
                await RejectAsync(context);
            }
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Authorization fails closed when current account state cannot be checked.
            logger.LogError("Authenticated account state validation failed; rejecting the cookie. UserId={UserId}, ErrorType={ErrorType}",
                userId, exception.GetType().Name);
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}
