using Microsoft.AspNetCore.Identity;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;

namespace NEXORA.Web.Identity;

public sealed class RoleSeeder(
    RoleManager<AppRole> roleManager,
    ILogger<RoleSeeder> logger)
{
    public async Task EnsureRolesAsync(CancellationToken cancellationToken = default)
    {
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
}
