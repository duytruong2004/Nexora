using Microsoft.AspNetCore.Identity;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Web.Identity;

public sealed record ProvisioningCommandArguments(
    bool IsRequested,
    bool IsDevelopmentInventorySeedRequested,
    bool IsQaStaffProvisioningRequested,
    string[] HostArguments)
{
    public static ProvisioningCommandArguments Parse(string[] args)
    {
        var commandIndexes = args
            .Select((value, index) => (value, index))
            .Where(item => string.Equals(item.value, "--provision-admin", StringComparison.Ordinal) ||
                           string.Equals(item.value, "--seed-development-inventory", StringComparison.Ordinal) ||
                           string.Equals(item.value, "--provision-qa-staff", StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();

        if (commandIndexes.Length == 0)
        {
            return new ProvisioningCommandArguments(false, false, false, args);
        }

        if (commandIndexes.Length != 1 || args.Length != 1)
        {
            throw new ArgumentException("Use one Development provisioning command by itself.", nameof(args));
        }

        return new ProvisioningCommandArguments(
            true,
            string.Equals(args[0], "--seed-development-inventory", StringComparison.Ordinal),
            string.Equals(args[0], "--provision-qa-staff", StringComparison.Ordinal),
            []);
    }
}

public static class DevelopmentAdminProvisioningCommand
{
    public static async Task<int> ExecuteAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (Console.IsInputRedirected)
        {
            logger.LogWarning("Admin provisioning requires an interactive local console.");
            return 2;
        }

        Console.Write("Email của tài khoản đã đăng ký: ");
        var email = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.WriteLine("Email không hợp lệ. Không có thay đổi nào được thực hiện.");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
        {
            Console.WriteLine("Không tìm thấy tài khoản đang hoạt động. Không có thay đổi nào được thực hiện.");
            return 3;
        }

        if (await userManager.IsInRoleAsync(user, RoleNames.Admin))
        {
            Console.WriteLine("Tài khoản đã có role Admin; không cần thay đổi.");
            logger.LogInformation("Admin provisioning was requested for an existing administrator. UserId={UserId}", user.Id);
            return 0;
        }

        Console.WriteLine($"Tài khoản được chọn: {user.FullName} (ID {user.Id}).");
        Console.Write($"Để xác nhận, nhập chính xác GRANT ADMIN {user.Id}: ");
        var confirmation = Console.ReadLine()?.Trim();
        if (!string.Equals(confirmation, $"GRANT ADMIN {user.Id}", StringComparison.Ordinal))
        {
            Console.WriteLine("Xác nhận không khớp. Không có thay đổi nào được thực hiện.");
            return 4;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await userManager.AddToRoleAsync(user, RoleNames.Admin);
        if (!result.Succeeded)
        {
            logger.LogError("Could not grant the Admin role. Identity error codes: {ErrorCodes}",
                string.Join(",", result.Errors.Select(error => error.Code)));
            Console.WriteLine("Không thể cấp role Admin. Không có mật khẩu hoặc token nào được ghi log.");
            return 5;
        }

        logger.LogWarning("Admin role granted through the local Development console. UserId={UserId}", user.Id);
        Console.WriteLine("Đã cấp role Admin cho tài khoản đã chọn.");
        return 0;
    }
}

public static class DevelopmentInventoryProvisioningCommand
{
    public static async Task<int> ExecuteAsync(
        IServiceProvider services,
        string? connectionString,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseConnectionPolicy.IsApprovedQaConnectionString(connectionString))
        {
            logger.LogError("Development inventory provisioning refused because the configured SQL target is not the approved QA database.");
            Console.WriteLine("Từ chối seed: chỉ chấp nhận Windows Authentication tới PC01\\SQLEXPRESS / NEXORA_IntegrationTests.");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<NEXORA.Application.Commerce.IDevelopmentInventorySeeder>();
        await seeder.SeedMissingAsync(cancellationToken);
        Console.WriteLine("Đã bổ sung bản ghi tồn mô phỏng còn thiếu; tồn hiện có được giữ nguyên.");
        logger.LogInformation("Development inventory seeding command completed.");
        return 0;
    }

    public static bool TargetsDevelopmentDatabase(string connectionString)
    {
        try
        {
            var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            var expectedServer = $@"{Environment.MachineName}\SQLEXPRESS";
            var server = connection.DataSource.Trim();
            var isLocalExpress = string.Equals(server, expectedServer, StringComparison.OrdinalIgnoreCase) ||
                                 (string.Equals(Environment.MachineName, "PC01", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(server, @".\SQLEXPRESS", StringComparison.OrdinalIgnoreCase));
            return isLocalExpress && string.Equals(connection.InitialCatalog, "NEXORA", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
