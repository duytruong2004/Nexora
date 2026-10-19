namespace NEXORA.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")))
        {
            Skip = "Set NEXORA_SQLSERVER_TEST_CONNECTION to run the Development SQL Server integration test.";
            return;
        }

        try
        {
            var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION"));
            var server = connection.DataSource.Replace("tcp:", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(connection.InitialCatalog, "NEXORA_IntegrationTests", StringComparison.OrdinalIgnoreCase) ||
                !server.Contains("PC01\\SQLEXPRESS", StringComparison.OrdinalIgnoreCase))
            {
                Skip = "SQL integration tests require the dedicated PC01\\SQLEXPRESS / NEXORA_IntegrationTests database.";
            }
        }
        catch (ArgumentException)
        {
            Skip = "NEXORA_SQLSERVER_TEST_CONNECTION is invalid; SQL integration tests were not run.";
        }
    }
}
