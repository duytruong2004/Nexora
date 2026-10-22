using Microsoft.Data.SqlClient;
using NEXORA.Domain.Security;
using NEXORA.Web.Identity;

namespace NEXORA.Tests;

public static class AccountAdministrationSqlServerTestSupport
{
    private static readonly string[] ExpectedMigrations =
    [
        "20261015173046_InitialFoundation",
        "20261016133620_AddCheckoutRequestId",
        "20261019174632_AddOrderCancellationRequestFields"
    ];

    public static async Task<string> GetVerifiedQaConnectionStringAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION");
        if (!QaStaffProvisioningCommand.IsApprovedQaConnectionString(connectionString))
        {
            throw new InvalidOperationException("SQL tests require PC01\\SQLEXPRESS / NEXORA_IntegrationTests with Windows Authentication.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var target = connection.CreateCommand())
        {
            target.CommandText = "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')), DB_NAME(), CONVERT(nvarchar(30),DATABASEPROPERTYEX(DB_NAME(),'Status'));";
            await using var reader = await target.ExecuteReaderAsync();
            if (!await reader.ReadAsync() ||
                !string.Equals(reader.GetString(0), "PC01\\SQLEXPRESS", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(reader.GetString(1), "NEXORA_IntegrationTests", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(reader.GetString(2), "ONLINE", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SQL Server returned an unexpected QA target or status.");
            }
        }

        await using (var migrationQuery = connection.CreateCommand())
        {
            migrationQuery.CommandText = "SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;";
            await using var reader = await migrationQuery.ExecuteReaderAsync();
            var actual = new List<string>();
            while (await reader.ReadAsync()) actual.Add(reader.GetString(0));
            if (!ExpectedMigrations.SequenceEqual(actual, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("QA database migration history differs from the approved baseline.");
            }
        }

        await using (var tableQuery = connection.CreateCommand())
        {
            tableQuery.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0;";
            if (Convert.ToInt32(await tableQuery.ExecuteScalarAsync()) != 27)
            {
                throw new InvalidOperationException("QA database table count differs from the approved 27-table baseline.");
            }
        }

        await using (var roleQuery = connection.CreateCommand())
        {
            roleQuery.CommandText = "SELECT name FROM dbo.roles ORDER BY name;";
            await using var reader = await roleQuery.ExecuteReaderAsync();
            var actual = new List<string>();
            while (await reader.ReadAsync()) actual.Add(reader.GetString(0));
            var expected = new[] { RoleNames.Admin, RoleNames.Customer, RoleNames.Sales, RoleNames.Warehouse };
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("QA database does not contain the approved four-role set.");
            }
        }

        return connectionString!;
    }

    public static string NewQaEmail(string scenario) => $"qa.phase61.{scenario}.{Guid.NewGuid():N}@nexora.test";

    public static string NewPassword() => $"Nx!Qa{Guid.NewGuid():N}aA1";
}
