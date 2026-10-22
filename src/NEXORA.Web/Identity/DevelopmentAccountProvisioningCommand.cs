using System.Data;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Web.Identity;

public sealed record DevelopmentDemoAccount(string RoleName, string Email, string FullName, string Password);

public enum ExistingDevelopmentAccountAction
{
    Create,
    KeepUnchanged,
    Refuse
}

/// <summary>
/// Explicitly provisions one local demo identity for each canonical role from User Secrets or environment
/// configuration. Existing identities are never reset, reactivated, renamed, or assigned another role.
/// </summary>
public static class DevelopmentAccountProvisioningCommand
{
    public static bool TryReadDefinitions(
        IConfiguration configuration,
        out IReadOnlyList<DevelopmentDemoAccount> accounts,
        out string error)
    {
        var section = configuration.GetSection("DevelopmentAccounts");
        var roleSections = section.GetChildren().ToDictionary(child => child.Key, StringComparer.Ordinal);
        if (roleSections.Count != RoleNames.All.Count || RoleNames.All.Any(role => !roleSections.ContainsKey(role)))
        {
            accounts = [];
            error = "Exactly four DevelopmentAccounts entries are required, one for each canonical role.";
            return false;
        }

        var result = new List<DevelopmentDemoAccount>(RoleNames.All.Count);
        var normalizedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in RoleNames.All)
        {
            var accountSection = roleSections[roleName];
            var email = accountSection["Email"]?.Trim();
            var fullName = accountSection["FullName"]?.Trim();
            var password = accountSection["Password"];
            if (string.IsNullOrWhiteSpace(email) || email.Length > 256 ||
                string.IsNullOrWhiteSpace(fullName) || fullName.Length > 200 ||
                string.IsNullOrEmpty(password))
            {
                accounts = [];
                error = "Each demo account needs an email, display name, and password in local secrets.";
                return false;
            }

            try
            {
                var address = new MailAddress(email);
                if (!string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(address.Host, "nexora.test", StringComparison.OrdinalIgnoreCase))
                {
                    accounts = [];
                    error = "Demo account email addresses must use the non-routable nexora.test domain.";
                    return false;
                }
            }
            catch (FormatException)
            {
                accounts = [];
                error = "A demo account email address is invalid.";
                return false;
            }

            if (!normalizedEmails.Add(email))
            {
                accounts = [];
                error = "Each canonical role needs a distinct demo account email address.";
                return false;
            }

            result.Add(new DevelopmentDemoAccount(roleName, email, fullName, password));
        }

        accounts = result;
        error = string.Empty;
        return true;
    }

    public static ExistingDevelopmentAccountAction DecideExistingAccount(
        bool exists,
        bool isActive,
        IEnumerable<string> currentRoles,
        string desiredRole)
    {
        if (!exists)
        {
            return ExistingDevelopmentAccountAction.Create;
        }

        var roles = currentRoles.ToArray();
        return isActive && roles.Length == 1 &&
               string.Equals(roles[0], desiredRole, StringComparison.Ordinal)
            ? ExistingDevelopmentAccountAction.KeepUnchanged
            : ExistingDevelopmentAccountAction.Refuse;
    }

    public static async Task<int> ExecuteAsync(
        IServiceProvider services,
        IConfiguration configuration,
        string? connectionString,
        string? databaseProfile,
        string? environmentName,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!DevelopmentSeedPolicy.IsAllowed(environmentName, databaseProfile, connectionString))
        {
            logger.LogError("Development account provisioning was refused because the target is not a personal local Development database.");
            Console.WriteLine("Từ chối tạo tài khoản demo: chỉ chạy trên database Development cá nhân đã được policy cho phép.");
            return 2;
        }

        if (!TryReadDefinitions(configuration, out var accounts, out var configurationError))
        {
            Console.WriteLine($"Cấu hình tài khoản demo chưa hợp lệ: {configurationError}");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            var result = await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

                var currentRoleNames = await roleManager.Roles
                    .Select(role => role.Name)
                    .ToListAsync(cancellationToken);
                if (!RoleSeeder.HasOnlyCanonicalRoleNames(currentRoleNames) ||
                    currentRoleNames.Count != RoleNames.All.Count)
                {
                    throw new DevelopmentAccountProvisioningException(
                        "database",
                        ["CanonicalRolesMismatch"]);
                }

                foreach (var account in accounts)
                {
                    if (!await roleManager.RoleExistsAsync(account.RoleName))
                    {
                        throw new DevelopmentAccountProvisioningException(
                            account.RoleName,
                            ["RequiredRoleMissing"]);
                    }
                }

                var hasMultiRoleAccount = await db.UserRoles.AsNoTracking()
                    .GroupBy(userRole => userRole.UserId)
                    .AnyAsync(group => group.Count() > 1, cancellationToken);
                if (hasMultiRoleAccount)
                {
                    throw new DevelopmentAccountProvisioningException(
                        "database",
                        ["MultiRoleAccountDetected"]);
                }

                var created = 0;
                var unchanged = 0;
                foreach (var definition in accounts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var existing = await userManager.FindByEmailAsync(definition.Email);
                    if (existing is not null)
                    {
                        var existingRoles = await userManager.GetRolesAsync(existing);
                        var action = DecideExistingAccount(
                            exists: true,
                            existing.IsActive,
                            existingRoles,
                            definition.RoleName);
                        if (action != ExistingDevelopmentAccountAction.KeepUnchanged)
                        {
                            throw new DevelopmentAccountProvisioningException(
                                definition.RoleName,
                                ["ExistingAccountDoesNotMatch"]);
                        }

                        unchanged++;
                        continue;
                    }

                    if (await userManager.FindByNameAsync(definition.Email) is not null)
                    {
                        throw new DevelopmentAccountProvisioningException(
                            definition.RoleName,
                            ["UserNameAlreadyExists"]);
                    }

                    var user = new AppUser
                    {
                        UserName = definition.Email,
                        Email = definition.Email,
                        EmailConfirmed = true,
                        FullName = definition.FullName,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        LockoutEnabled = true
                    };

                    var createResult = await userManager.CreateAsync(user, definition.Password);
                    if (!createResult.Succeeded)
                    {
                        throw new DevelopmentAccountProvisioningException(
                            definition.RoleName,
                            createResult.Errors.Select(error => error.Code).ToArray());
                    }

                    var roleResult = await userManager.AddToRoleAsync(user, definition.RoleName);
                    if (!roleResult.Succeeded)
                    {
                        throw new DevelopmentAccountProvisioningException(
                            definition.RoleName,
                            roleResult.Errors.Select(error => error.Code).ToArray());
                    }

                    created++;
                }

                await transaction.CommitAsync(cancellationToken);
                return (Created: created, Unchanged: unchanged);
            });

            logger.LogInformation(
                "Development demo account setup completed. CreatedCount={CreatedCount}; UnchangedCount={UnchangedCount}.",
                result.Created,
                result.Unchanged);
            Console.WriteLine($"Tạo mới: {result.Created}; tài khoản khớp sẵn được giữ nguyên: {result.Unchanged}. Không có mật khẩu được ghi log.");
            return 0;
        }
        catch (DevelopmentAccountProvisioningException exception)
        {
            logger.LogWarning(
                "Development account setup was refused or rolled back. Role={RoleName}; IdentityErrorCodes={ErrorCodes}.",
                exception.RoleName,
                string.Join(",", exception.ErrorCodes));
            Console.WriteLine("Tài khoản demo hiện có không khớp role dự kiến hoặc Identity từ chối dữ liệu. Thao tác đã rollback; không reset hay đổi quyền tài khoản cũ.");
            return 4;
        }
        catch (Exception exception)
        {
            logger.LogError("Development account setup failed. ExceptionType={ExceptionType}.", exception.GetType().Name);
            Console.WriteLine("Tạo tài khoản demo thất bại. Giao dịch được rollback khi có thể; không có email hoặc mật khẩu nào được ghi log.");
            return 5;
        }
    }

    private sealed class DevelopmentAccountProvisioningException(
        string roleName,
        IReadOnlyList<string> errorCodes) : Exception
    {
        public string RoleName { get; } = roleName;
        public IReadOnlyList<string> ErrorCodes { get; } = errorCodes;
    }
}
