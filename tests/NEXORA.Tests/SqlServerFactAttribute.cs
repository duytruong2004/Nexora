using NEXORA.Web.Identity;

namespace NEXORA.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Skip = "Set NEXORA_SQLSERVER_TEST_CONNECTION to run the Development SQL Server integration test.";
            return;
        }

        if (!QaStaffProvisioningCommand.IsApprovedQaConnectionString(connectionString))
        {
            Skip = "SQL integration tests require Windows Authentication to exactly PC01\\SQLEXPRESS / NEXORA_IntegrationTests.";
        }
    }
}
