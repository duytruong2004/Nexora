using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;

namespace NEXORA.Web.Identity;

public sealed class RoleSeeder(
    RoleManager<AppRole> roleManager,
    ILogger<RoleSeeder> logger)
{
    public async Task EnsureRolesAsync(CancellationToken cancellationToken = default)
    {
        var existingNames = await roleManager.Roles
            .Select(role => role.Name)
            .ToListAsync(cancellationToken);
        if (!HasOnlyCanonicalRoleNames(existingNames))
        {
            logger.LogError("Role initialization refused because the personal Development database contains a non-canonical role name.");
            throw new InvalidOperationException("The local database contains a role outside the four canonical NEXORA roles.");
        }

        foreach (var roleName in RoleNames.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new AppRole
            {
                Name = roleName,
                Description = $"NEXORA {roleName} role",
                CreatedAt = DateTime.UtcNow
            });

            if (!result.Succeeded)
            {
                logger.LogError("Could not ensure a required NEXORA role. Identity error codes: {ErrorCodes}",
                    string.Join(",", result.Errors.Select(error => error.Code)));
                throw new InvalidOperationException("Required NEXORA roles could not be initialized.");
            }

            logger.LogInformation("Initialized NEXORA role {RoleName}.", roleName);
        }
    }

    public static bool HasOnlyCanonicalRoleNames(IEnumerable<string?> roleNames)
    {
        var names = roleNames.ToArray();
        return names.All(name => name is not null && RoleNames.All.Contains(name, StringComparer.Ordinal)) &&
               names.Distinct(StringComparer.Ordinal).Count() == names.Length;
    }
}
