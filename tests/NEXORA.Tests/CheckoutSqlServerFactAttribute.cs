using Microsoft.Data.SqlClient;

namespace NEXORA.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class CheckoutSqlServerFactAttribute : FactAttribute
{
    public CheckoutSqlServerFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NEXORA_CHECKOUT_SQL_TESTS"), "1", StringComparison.Ordinal))
        {
            Skip = "Requires explicit opt-in after AddCheckoutRequestId is applied to the dedicated integration database.";
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Skip = "Set NEXORA_SQLSERVER_TEST_CONNECTION to the dedicated integration database.";
            return;
        }

        try
        {
            var connection = new SqlConnectionStringBuilder(connectionString);
            var server = connection.DataSource.Replace("tcp:", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(connection.InitialCatalog, "NEXORA_IntegrationTests", StringComparison.OrdinalIgnoreCase) ||
                !server.Contains("PC01\\SQLEXPRESS", StringComparison.OrdinalIgnoreCase))
            {
                Skip = "Checkout SQL tests require PC01\\SQLEXPRESS / NEXORA_IntegrationTests.";
            }
        }
        catch (ArgumentException)
        {
            Skip = "NEXORA_SQLSERVER_TEST_CONNECTION is invalid; checkout SQL tests were not run.";
        }
    }
}
