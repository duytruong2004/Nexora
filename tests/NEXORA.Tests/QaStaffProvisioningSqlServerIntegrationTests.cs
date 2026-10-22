using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;
using NEXORA.Web.Identity;

namespace NEXORA.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class QaStaffProvisioningSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task Provisioning_SalesAndWarehouseCreatesIdentityAndRoleAtomically_UsingRetryingExecutionStrategy()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString);
        var salesEmail = NewQaEmail("sales");
        var warehouseEmail = NewQaEmail("warehouse");

        try
        {
            var sales = await ProvisionAsync(provider, salesEmail, RoleNames.Sales);
            var warehouse = await ProvisionAsync(provider, warehouseEmail, RoleNames.Warehouse);

            Assert.Equal(QaStaffProvisioningStatus.Created, sales.Status);
            Assert.Equal(QaStaffProvisioningStatus.Created, warehouse.Status);
            Assert.NotNull(sales.UserId);
            Assert.NotNull(warehouse.UserId);
            await AssertHasExactlyRoleAsync(provider, salesEmail, RoleNames.Sales);
            await AssertHasExactlyRoleAsync(provider, warehouseEmail, RoleNames.Warehouse);
        }
        finally
        {
            await DeleteQaUserIfPresentAsync(provider, salesEmail);
            await DeleteQaUserIfPresentAsync(provider, warehouseEmail);
        }
    }

    [SqlServerFact]
    public async Task Provisioning_RollsBackCreatedIdentityWhenRoleAssignmentIsRejected()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString);
        var email = NewQaEmail("rollback");

        try
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();

            var result = await QaStaffProvisioningCommand.ProvisionAccountAsync(
                db,
                userManager,
                roleManager,
                email,
                "NEXORA QA rollback fixture",
                    NewPassword(),
                RoleNames.Sales,
                roleAssignment: (_, _) => Task.FromResult(IdentityResult.Failed(
                    new IdentityError { Code = "QA_TEST_ROLE_ASSIGNMENT_REJECTED" })));

            Assert.Equal(QaStaffProvisioningStatus.IdentityRejected, result.Status);
            Assert.Equal("role-assignment", result.FailedStage);
            Assert.Contains("QA_TEST_ROLE_ASSIGNMENT_REJECTED", result.ErrorCodes ?? []);
            Assert.Null(await userManager.FindByEmailAsync(email));
            Assert.False(await db.Users.AsNoTracking().AnyAsync(user => user.Email == email));
        }
        finally
        {
            await DeleteQaUserIfPresentAsync(provider, email);
        }
    }

    [SqlServerFact]
    public async Task Provisioning_IsIdempotentForTheSameQaAddressAndNeverPromotesExistingCustomer()
    {
        var connectionString = await GetVerifiedQaConnectionStringAsync();
        await using var provider = BuildProvider(connectionString);
        var newEmail = NewQaEmail("duplicate");
        var customerEmail = NewQaEmail("customer");

        try
        {
            var first = await ProvisionAsync(provider, newEmail, RoleNames.Sales);
            var second = await ProvisionAsync(provider, newEmail, RoleNames.Sales);
            Assert.Equal(QaStaffProvisioningStatus.Created, first.Status);
            Assert.Equal(QaStaffProvisioningStatus.AlreadyProvisioned, second.Status);
            Assert.Equal(first.UserId, second.UserId);

            await using (var scope = provider.CreateAsyncScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                var customer = new AppUser
                {
                    UserName = customerEmail,
                    Email = customerEmail,
                    FullName = "NEXORA QA Customer fixture",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                var created = await userManager.CreateAsync(customer, NewPassword());
                Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(error => error.Code)));
                var assigned = await userManager.AddToRoleAsync(customer, RoleNames.Customer);
                Assert.True(assigned.Succeeded, string.Join(",", assigned.Errors.Select(error => error.Code)));
            }

            var attemptToPromote = await ProvisionAsync(provider, customerEmail, RoleNames.Sales);
            Assert.Equal(QaStaffProvisioningStatus.AccountConflict, attemptToPromote.Status);
            await AssertHasExactlyRoleAsync(provider, customerEmail, RoleNames.Customer);
            await AssertHasExactlyRoleAsync(provider, newEmail, RoleNames.Sales);
        }
        finally
        {
            await DeleteQaUserIfPresentAsync(provider, newEmail);
            await DeleteQaUserIfPresentAsync(provider, customerEmail);
        }
    }

    private static async Task<QaStaffProvisioningResult> ProvisionAsync(
        ServiceProvider provider,
        string email,
        string role)
    {
        await using var scope = provider.CreateAsyncScope();
        return await QaStaffProvisioningCommand.ProvisionAccountAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>(),
            email,
            "NEXORA QA provisioning fixture",
            NewPassword(),
            role);
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexoraInfrastructure(connectionString, "QA");
        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredUniqueChars = 4;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task AssertHasExactlyRoleAsync(ServiceProvider provider, string email, string roleName)
    {
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        var roles = await userManager.GetRolesAsync(user);
        Assert.Equal([roleName], roles);
    }

    private static async Task DeleteQaUserIfPresentAsync(ServiceProvider provider, string email)
    {
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            var result = await userManager.DeleteAsync(user);
            Assert.True(result.Succeeded, string.Join(",", result.Errors.Select(error => error.Code)));
        }
    }

    private static async Task<string> GetVerifiedQaConnectionStringAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION");
        Assert.True(QaStaffProvisioningCommand.IsApprovedQaConnectionString(connectionString),
            "Provisioning regression tests require Windows Authentication to PC01\\SQLEXPRESS / NEXORA_IntegrationTests.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')), DB_NAME(), CONVERT(nvarchar(30), DATABASEPROPERTYEX(DB_NAME(),'Status'));";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("PC01\\SQLEXPRESS", reader.GetString(0), ignoreCase: true);
            Assert.Equal("NEXORA_IntegrationTests", reader.GetString(1), ignoreCase: true);
            Assert.Equal("ONLINE", reader.GetString(2), ignoreCase: true);
        }

        var expectedMigrations = new[]
        {
            "20261015173046_InitialFoundation",
            "20261016133620_AddCheckoutRequestId",
            "20261019174632_AddOrderCancellationRequestFields"
        };
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;";
            await using var reader = await command.ExecuteReaderAsync();
            var actual = new List<string>();
            while (await reader.ReadAsync()) actual.Add(reader.GetString(0));
            Assert.Equal(expectedMigrations, actual);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;";
            Assert.Equal(27, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }

        return connectionString!;
    }

    private static string NewQaEmail(string scenario) => $"qa.provisioning.{scenario}.{Guid.NewGuid():N}@nexora.test";

    private static string NewPassword() => $"Nexora!Qa{Guid.NewGuid():N}";
}
