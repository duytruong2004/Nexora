using NEXORA.Web.Identity;

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

        if (!QaStaffProvisioningCommand.IsApprovedQaConnectionString(connectionString))
        {
            Skip = "Checkout SQL tests require Windows Authentication to exactly PC01\\SQLEXPRESS / NEXORA_IntegrationTests.";
        }
    }
}
