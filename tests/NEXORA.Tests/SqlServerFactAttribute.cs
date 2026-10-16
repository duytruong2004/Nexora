namespace NEXORA.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")))
        {
            Skip = "Set NEXORA_SQLSERVER_TEST_CONNECTION to run the Development SQL Server integration test.";
        }
    }
}
