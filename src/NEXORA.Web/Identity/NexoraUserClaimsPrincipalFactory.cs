using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NEXORA.Infrastructure.Identity;

namespace NEXORA.Web.Identity;

public sealed class NexoraUserClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, AppRole>(userManager, roleManager, options)
{
    public const string FullNameClaimType = "nexora:full_name";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        var fullName = user.FullName?.Trim();
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            identity.AddClaim(new Claim(FullNameClaimType, fullName));
        }

        return identity;
    }
}
