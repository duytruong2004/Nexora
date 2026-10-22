using NEXORA.Web.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Tests;

public sealed class AdminProvisioningCommandTests
{
    [Fact]
    public void Parse_WithoutProvisioningCommand_PreservesHostArguments()
    {
        var parsed = ProvisioningCommandArguments.Parse(["--urls", "http://127.0.0.1:5000"]);

        Assert.False(parsed.IsRequested);
        Assert.Equal(["--urls", "http://127.0.0.1:5000"], parsed.HostArguments);
        Assert.False(parsed.IsQaStaffProvisioningRequested);
    }

    [Fact]
    public void Parse_ExplicitProvisioningCommand_ConsumesTheCommand()
    {
        var parsed = ProvisioningCommandArguments.Parse(["--provision-admin"]);

        Assert.True(parsed.IsRequested);
        Assert.False(parsed.IsDevelopmentInventorySeedRequested);
        Assert.False(parsed.IsQaStaffProvisioningRequested);
        Assert.Empty(parsed.HostArguments);
    }

    [Fact]
    public void Parse_DevelopmentInventorySeedCommandConsumesOnlyTheStandaloneCommand()
    {
        var parsed = ProvisioningCommandArguments.Parse(["--seed-development-inventory"]);

        Assert.True(parsed.IsRequested);
        Assert.True(parsed.IsDevelopmentInventorySeedRequested);
        Assert.False(parsed.IsQaStaffProvisioningRequested);
        Assert.Empty(parsed.HostArguments);
    }

    [Fact]
    public void Parse_QaStaffProvisioningCommandConsumesOnlyTheStandaloneCommand()
    {
        var parsed = ProvisioningCommandArguments.Parse(["--provision-qa-staff"]);

        Assert.True(parsed.IsRequested);
        Assert.False(parsed.IsDevelopmentInventorySeedRequested);
        Assert.True(parsed.IsQaStaffProvisioningRequested);
        Assert.Empty(parsed.HostArguments);
    }

    [Fact]
    public void DevelopmentInventoryCommand_UsesApprovedQaPolicyAndRejectsMainDatabaseForDevelopment()
    {
        const string approvedQa = "Server=PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True;TrustServerCertificate=True";
        const string mainDatabase = "Server=PC01\\SQLEXPRESS;Database=NEXORA;Integrated Security=True;TrustServerCertificate=True";
        const string unapprovedServer = "Server=OTHER\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True;TrustServerCertificate=True";

        Assert.True(DatabaseConnectionPolicy.IsApprovedQaConnectionString(approvedQa));
        Assert.False(DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase(approvedQa));
        Assert.False(DatabaseConnectionPolicy.IsApprovedQaConnectionString(mainDatabase));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.Resolve(mainDatabase, "Development"));
        Assert.False(DatabaseConnectionPolicy.IsApprovedQaConnectionString(unapprovedServer));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.Resolve(unapprovedServer, "Development"));
        Assert.False(DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase("not a connection string"));
    }

    [Fact]
    public void Parse_ProvisioningCommandWithArguments_RequiresInteractiveStandaloneInvocation()
    {
        Assert.Throws<ArgumentException>(() =>
            ProvisioningCommandArguments.Parse(["--provision-admin", "admin@example.test"]));
    }

    [Fact]
    public void QaStaffProvisioning_OnlyAcceptsTheDedicatedIntegratedSecurityDatabase()
    {
        Assert.True(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True;TrustServerCertificate=True"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=PC01\\SQLEXPRESS;Database=NEXORA;Integrated Security=True;TrustServerCertificate=True"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=OTHER\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=OTHER-PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=.\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString(
            "Server=PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=False;User ID=qa"));
        Assert.False(QaStaffProvisioningCommand.IsApprovedQaConnectionString("not a connection string"));
    }

    [Theory]
    [InlineData("qa.sales@nexora.test", true)]
    [InlineData("qa.warehouse@nexora.test", true)]
    [InlineData("user@gmail.com", false)]
    [InlineData("qa.user@example.com", false)]
    [InlineData("sales@nexora.test", false)]
    [InlineData("qa.user@nexora.com", false)]
    [InlineData("not-an-email", false)]
    public void QaStaffProvisioning_OnlyAcceptsNonRoutableQaEmailAddresses(string email, bool expected)
    {
        Assert.Equal(expected, QaStaffProvisioningCommand.IsQaEmailAddress(email));
    }
}
