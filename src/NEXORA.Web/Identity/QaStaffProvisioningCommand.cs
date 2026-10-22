using System.Net.Mail;
using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Web.Identity;

/// <summary>
/// Local-only tool for creating new QA staff identities. It deliberately cannot modify an existing account
/// and accepts only the Sales and Warehouse roles and non-routable @nexora.test addresses.
/// </summary>
public static class QaStaffProvisioningCommand
{
    private const string ExpectedServer = DatabaseConnectionPolicy.QaServer;
    private const string ExpectedDatabase = DatabaseConnectionPolicy.QaDatabase;
    private static readonly string[] ExpectedMigrations =
    [
        "20261015173046_InitialFoundation",
        "20261016133620_AddCheckoutRequestId",
        "20261019174632_AddOrderCancellationRequestFields"
    ];

    private static readonly string[] BusinessTables =
    [
        "roles", "users", "user_roles", "categories", "brands", "products", "product_images",
        "specifications", "category_specifications", "product_specifications", "carts", "cart_items",
        "orders", "order_details", "inventories", "inventory_transactions", "favorites", "reviews",
        "promotions", "promotion_products", "usage_needs", "recommendation_rules"
    ];

    public static bool IsApprovedQaConnectionString(string? connectionString)
    {
        return DatabaseConnectionPolicy.IsApprovedQaConnectionString(connectionString);
    }

    public static bool IsQaEmailAddress(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256) return false;

        try
        {
            var parsed = new MailAddress(email.Trim());
            var at = parsed.Address.LastIndexOf('@');
            return string.Equals(parsed.Address, email.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   at > 0 &&
                   parsed.Address[..at].StartsWith("qa.", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(parsed.Address[(at + 1)..], "nexora.test", StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static async Task<int> ExecuteAsync(
        IServiceProvider services,
        string? connectionString,
        IHostEnvironment environment,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("QA"))
        {
            Console.WriteLine("Từ chối: lệnh này chỉ chạy trong môi trường Development hoặc QA.");
            return 2;
        }

        if (!IsApprovedQaConnectionString(connectionString))
        {
            Console.WriteLine("Từ chối: cấu hình phải dùng Windows Authentication tới PC01\\SQLEXPRESS / NEXORA_IntegrationTests.");
            return 2;
        }
        var approvedConnectionString = connectionString!;

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.WriteLine("Từ chối: cần chạy trong terminal cục bộ tương tác; không có thay đổi nào được thực hiện.");
            return 2;
        }

        var preflight = await VerifyQaDatabaseAsync(approvedConnectionString, cancellationToken);
        if (preflight is not null)
        {
            Console.WriteLine($"Từ chối provisioning: {preflight} Không có thay đổi nào được thực hiện.");
            return 3;
        }

        Console.WriteLine("Đã xác minh PC01\\SQLEXPRESS / NEXORA_IntegrationTests, ONLINE, đúng 27 bảng và ba migration đã duyệt.");
        Console.WriteLine("Lệnh chỉ tạo tài khoản QA mới cho Sales hoặc Warehouse; không sửa tài khoản hiện có.");
        Console.Write("Vai trò cần tạo (Sales/Warehouse): ");
        var roleInput = Console.ReadLine()?.Trim();
        var roleName = string.Equals(roleInput, RoleNames.Sales, StringComparison.OrdinalIgnoreCase)
            ? RoleNames.Sales
            : string.Equals(roleInput, RoleNames.Warehouse, StringComparison.OrdinalIgnoreCase)
                ? RoleNames.Warehouse
                : null;
        if (roleName is null)
        {
            Console.WriteLine("Vai trò không hợp lệ. Không có thay đổi nào được thực hiện.");
            return 4;
        }

        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await roleManager.RoleExistsAsync(roleName))
        {
            Console.WriteLine("Role NEXORA đã duyệt không tồn tại. Lệnh không tự tạo hoặc sửa role.");
            return 5;
        }

        Console.Write("Email QA mới (chỉ chấp nhận dạng qa.*@nexora.test): ");
        var email = Console.ReadLine()?.Trim();
        if (!IsQaEmailAddress(email))
        {
            Console.WriteLine("Email không thuộc không gian QA @nexora.test. Không có thay đổi nào được thực hiện.");
            return 4;
        }
        var qaEmail = email!;

        Console.Write("Tên hiển thị QA: ");
        var fullName = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 200)
        {
            Console.WriteLine("Tên hiển thị không hợp lệ. Không có thay đổi nào được thực hiện.");
            return 4;
        }

        if (await userManager.FindByEmailAsync(qaEmail) is not null ||
            await userManager.FindByNameAsync(qaEmail) is not null)
        {
            Console.WriteLine("Email hoặc username đã tồn tại. Lệnh không thay đổi tài khoản hiện có.");
            return 6;
        }

        Console.WriteLine("Nhập mật khẩu theo chính sách Identity; nội dung sẽ không hiển thị và không được ghi log.");
        var password = ReadHiddenPassword("Mật khẩu: ");
        var confirmation = ReadHiddenPassword("Nhập lại mật khẩu: ");
        if (password is null || confirmation is null || !string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.WriteLine("Mật khẩu bị hủy hoặc không khớp. Không có thay đổi nào được thực hiện.");
            return 4;
        }

        Console.Write($"Xác nhận tạo tài khoản QA role {roleName}; nhập chính xác CREATE QA {roleName}: ");
        if (!string.Equals(Console.ReadLine()?.Trim(), $"CREATE QA {roleName}", StringComparison.Ordinal))
        {
            Console.WriteLine("Xác nhận không khớp. Không có thay đổi nào được thực hiện.");
            return 4;
        }

        try
        {
            var result = await ProvisionAccountAsync(
                db,
                userManager,
                roleManager,
                qaEmail,
                fullName,
                password,
                roleName,
                cancellationToken);

            if (result.Status == QaStaffProvisioningStatus.Created)
            {
                logger.LogInformation("A new QA staff identity was provisioned. UserId={UserId}, Role={RoleName}.", result.UserId, roleName);
                Console.WriteLine($"Đã tạo tài khoản QA mới (UserId {result.UserId}) với role {roleName}. Mật khẩu không được hiển thị hoặc ghi log.");
                return 0;
            }

            if (result.Status == QaStaffProvisioningStatus.AlreadyProvisioned)
            {
                Console.WriteLine($"Tài khoản QA đã có role {roleName}; không tạo bản ghi trùng và không thay đổi tài khoản.");
                return 0;
            }

            if (result.Status == QaStaffProvisioningStatus.IdentityRejected)
            {
                logger.LogWarning("Identity rejected QA staff provisioning. Stage={Stage}, ErrorCodes={ErrorCodes}.", result.FailedStage, string.Join(",", result.ErrorCodes ?? []));
                Console.WriteLine($"Identity từ chối thao tác ({result.FailedStage}): {string.Join(", ", result.ErrorCodes ?? [])}. Giao dịch đã rollback.");
                return 7;
            }

            Console.WriteLine("Tài khoản email này đã tồn tại nhưng không thuộc trạng thái QA được yêu cầu. Không có thay đổi nào được thực hiện.");
            return 6;
        }
        catch (Exception exception)
        {
            logger.LogError("QA staff provisioning failed. ExceptionType={ExceptionType}.", exception.GetType().Name);
            Console.WriteLine("Provisioning thất bại; giao dịch đã được xử lý bởi execution strategy. Không có mật khẩu hoặc email nào được ghi log.");
            return 8;
        }
        finally
        {
            password = null;
            confirmation = null;
        }
    }

    /// <summary>
    /// Creates the identity row and its role mapping in one retryable EF Core transaction.
    /// The callback exists only as an internal test seam for simulating an Identity role-assignment rejection.
    /// </summary>
    internal static async Task<QaStaffProvisioningResult> ProvisionAccountAsync(
        AppDbContext db,
        UserManager<AppUser> userManager,
        RoleManager<AppRole> roleManager,
        string email,
        string fullName,
        string password,
        string roleName,
        CancellationToken cancellationToken = default,
        Func<AppUser, string, Task<IdentityResult>>? roleAssignment = null)
    {
        if (!IsQaEmailAddress(email) || string.IsNullOrWhiteSpace(fullName) || fullName.Length > 200 ||
            roleName is not (RoleNames.Sales or RoleNames.Warehouse))
        {
            return new QaStaffProvisioningResult(QaStaffProvisioningStatus.InvalidRequest);
        }

        var normalizedEmail = userManager.NormalizeEmail(email);
        var normalizedUserName = userManager.NormalizeName(email);
        var normalizedRoleName = roleManager.NormalizeKey(roleName);
        var operationResult = new QaStaffProvisioningResult(QaStaffProvisioningStatus.Failed);
        var userId = 0;
        var strategy = db.Database.CreateExecutionStrategy();

        try
        {
            var executionResult = await Microsoft.EntityFrameworkCore.Storage.RelationalExecutionStrategyExtensions
                .ExecuteInTransactionAsync<QaStaffProvisioningResult>(
                strategy,
                async token =>
                {
                    // Identity's EF stores resolve the same scoped AppDbContext as this command.
                    // Clear state before every retry so a rolled-back attempt cannot leak tracked entities.
                    db.ChangeTracker.Clear();
                    userId = 0;
                    operationResult = new QaStaffProvisioningResult(QaStaffProvisioningStatus.Failed);

                    if (db.Database.CurrentTransaction is null)
                    {
                        throw new InvalidOperationException("EF Core did not start the provisioning transaction.");
                    }

                    var existing = await db.Users.SingleOrDefaultAsync(
                        user => user.NormalizedEmail == normalizedEmail || user.NormalizedUserName == normalizedUserName,
                        token);
                    if (existing is not null)
                    {
                        var alreadyHasRequestedRole = await (
                            from userRole in db.UserRoles
                            join role in db.Roles on userRole.RoleId equals role.Id
                            where userRole.UserId == existing.Id && role.NormalizedName == normalizedRoleName
                            select userRole.UserId).AnyAsync(token);

                        userId = existing.Id;
                        operationResult = new QaStaffProvisioningResult(
                            alreadyHasRequestedRole ? QaStaffProvisioningStatus.AlreadyProvisioned : QaStaffProvisioningStatus.AccountConflict,
                            userId);
                        return operationResult;
                    }

                    var user = new AppUser
                    {
                        UserName = email,
                        Email = email,
                        FullName = fullName,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    var created = await userManager.CreateAsync(user, password);
                    if (!created.Succeeded)
                    {
                        operationResult = new QaStaffProvisioningResult(
                            QaStaffProvisioningStatus.IdentityRejected,
                            FailedStage: "create",
                            ErrorCodes: created.Errors.Select(error => error.Code).ToArray());
                        throw new IdentityProvisioningRejectedException();
                    }

                    var assigned = roleAssignment is null
                        ? await userManager.AddToRoleAsync(user, roleName)
                        : await roleAssignment(user, roleName);
                    if (!assigned.Succeeded)
                    {
                        operationResult = new QaStaffProvisioningResult(
                            QaStaffProvisioningStatus.IdentityRejected,
                            FailedStage: "role-assignment",
                            ErrorCodes: assigned.Errors.Select(error => error.Code).ToArray());
                        throw new IdentityProvisioningRejectedException();
                    }

                    userId = user.Id;
                    operationResult = new QaStaffProvisioningResult(QaStaffProvisioningStatus.Created, userId);
                    return operationResult;
                },
                async token =>
                {
                    // Called by EF Core after an ambiguous commit. A retry is suppressed only when
                    // both the QA identity and its requested role mapping are visible in SQL Server.
                    db.ChangeTracker.Clear();
                    var committedUserId = await (
                        from user in db.Users
                        join userRole in db.UserRoles on user.Id equals userRole.UserId
                        join role in db.Roles on userRole.RoleId equals role.Id
                        where user.NormalizedEmail == normalizedEmail && role.NormalizedName == normalizedRoleName
                        select user.Id).FirstOrDefaultAsync(token);

                    if (committedUserId == 0)
                    {
                        return false;
                    }

                    userId = committedUserId;
                    operationResult = new QaStaffProvisioningResult(QaStaffProvisioningStatus.Created, userId);
                    return true;
                },
                IsolationLevel.ReadCommitted,
                cancellationToken);

            return executionResult;
        }
        catch (IdentityProvisioningRejectedException)
        {
            db.ChangeTracker.Clear();
            return operationResult;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // A concurrent request may win a unique-index race after our in-transaction pre-check.
            // Re-read only after the failed transaction has been disposed; never modify the winner.
            db.ChangeTracker.Clear();
            var existing = await db.Users.AsNoTracking().SingleOrDefaultAsync(
                user => user.NormalizedEmail == normalizedEmail || user.NormalizedUserName == normalizedUserName,
                cancellationToken);
            if (existing is not null)
            {
                var hasRequestedRole = await (
                    from userRole in db.UserRoles.AsNoTracking()
                    join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                    where userRole.UserId == existing.Id && role.NormalizedName == normalizedRoleName
                    select userRole.UserId).AnyAsync(cancellationToken);
                return new QaStaffProvisioningResult(
                    hasRequestedRole ? QaStaffProvisioningStatus.AlreadyProvisioned : QaStaffProvisioningStatus.AccountConflict,
                    existing.Id);
            }

            throw;
        }
    }

    private static async Task<string?> VerifyQaDatabaseAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var targetCommand = connection.CreateCommand())
            {
                targetCommand.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')), DB_NAME(), CONVERT(nvarchar(30), DATABASEPROPERTYEX(DB_NAME(), 'Status'));";
                await using var reader = await targetCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken) ||
                    !string.Equals(reader.GetString(0), ExpectedServer, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(reader.GetString(1), ExpectedDatabase, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(reader.GetString(2), "ONLINE", StringComparison.OrdinalIgnoreCase))
                {
                    return "SQL Server xác nhận server/database/trạng thái không khớp mục tiêu QA.";
                }
            }

            var migrations = new List<string>();
            await using (var migrationCommand = connection.CreateCommand())
            {
                migrationCommand.CommandText = "SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;";
                await using var reader = await migrationCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) migrations.Add(reader.GetString(0));
            }
            if (!migrations.SequenceEqual(ExpectedMigrations, StringComparer.Ordinal))
                return "Migration history không khớp chính xác baseline đã duyệt.";

            await using (var schemaCommand = connection.CreateCommand())
            {
                var businessNames = string.Join(",", BusinessTables.Select(name => $"'{name}'"));
                schemaCommand.CommandText = $"""
                    SELECT COUNT(*) AS PhysicalTableCount,
                           SUM(CASE WHEN name IN ({businessNames}) THEN 1 ELSE 0 END) AS BusinessTableCount
                    FROM sys.tables WHERE is_ms_shipped = 0;
                    SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.orders') AND name IN ('cancel_request_status','completed_at');
                    SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.inventories') AND name = 'row_version';
                    """;
                await using var reader = await schemaCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken) || reader.GetInt32(0) != 27 || reader.GetInt32(1) != 22)
                    return "Số bảng vật lý/nghiệp vụ không khớp baseline QA.";
                if (!await reader.NextResultAsync(cancellationToken) || !await reader.ReadAsync(cancellationToken) || reader.GetInt32(0) != 2)
                    return "Các cột Phase 5 trên orders không khớp baseline QA.";
                if (!await reader.NextResultAsync(cancellationToken) || !await reader.ReadAsync(cancellationToken) || reader.GetInt32(0) != 1)
                    return "Cột inventories.row_version không khớp baseline QA.";
            }

            var roles = new List<string>();
            await using (var roleCommand = connection.CreateCommand())
            {
                roleCommand.CommandText = "SELECT name FROM dbo.roles ORDER BY name;";
                await using var reader = await roleCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) roles.Add(reader.GetString(0));
            }

            var expectedRoles = RoleNames.All.Order(StringComparer.Ordinal).ToArray();
            if (!roles.Order(StringComparer.Ordinal).SequenceEqual(expectedRoles, StringComparer.Ordinal))
                return "Danh sách role trong QA không bằng đúng bốn role NEXORA đã duyệt.";

            return null;
        }
        catch (Exception exception)
        {
            return $"Không thể hoàn tất preflight chỉ đọc ({exception.GetType().Name}).";
        }
    }

    private static string? ReadHiddenPassword(string prompt)
    {
        Console.Write(prompt);
        var buffer = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string(buffer.ToArray());
            }
            if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                return null;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Count > 0) buffer.RemoveAt(buffer.Count - 1);
                continue;
            }
            if (!char.IsControl(key.KeyChar) && buffer.Count < 256) buffer.Add(key.KeyChar);
        }
    }

    private sealed class IdentityProvisioningRejectedException : Exception { }
}

internal enum QaStaffProvisioningStatus
{
    Failed,
    InvalidRequest,
    Created,
    AlreadyProvisioned,
    AccountConflict,
    IdentityRejected
}

internal sealed record QaStaffProvisioningResult(
    QaStaffProvisioningStatus Status,
    int? UserId = null,
    string? FailedStage = null,
    IReadOnlyList<string>? ErrorCodes = null);
