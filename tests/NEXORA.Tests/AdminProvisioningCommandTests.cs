using NEXORA.Web.Identity;

namespace NEXORA.Tests;

public sealed class AdminProvisioningCommandTests
{
    [Fact]
    public void Parse_WithoutProvisioningCommand_PreservesHostArguments()
    {
        var parsed = ProvisioningCommandArguments.Parse(["--urls", "http://127.0.0.1:5000"]);

        Assert.False(parsed.IsRequested);
        Assert.Equal(["--urls", "http://127.0.0.1:5000"], parsed.HostArguments);
    }

    [Fact]
    public void Parse_ExplicitProvisioningCommand_ConsumesTheCommand()
    {
        var parsed = ProvisioningCommandArguments.Parse(["--provision-admin"]);

        Assert.True(parsed.IsRequested);
        Assert.False(parsed.IsDevelopmentInventorySeedRequested);
        Assert.Empty(parsed.HostArguments);
    }

    [Fact]
    public void Parse_DevelopmentInventorySeedCommandConsumesOnlyTheStandaloneCommand()
    {
        var parsed = ProvisioningCommandArguments.Parse(["--seed-development-inventory"]);

        Assert.True(parsed.IsRequested);
        Assert.True(parsed.IsDevelopmentInventorySeedRequested);
        Assert.Empty(parsed.HostArguments);
    }

    [Fact]
    public void DevelopmentInventoryCommand_OnlyAcceptsTheConfiguredDevelopmentDatabase()
    {
        Assert.True(DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase(
            "Server=PC01\\SQLEXPRESS;Database=NEXORA;Integrated Security=True;TrustServerCertificate=True"));
        Assert.False(DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase(
            "Server=PC01\\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True"));
        Assert.False(DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase(
            "Server=OTHER\\SQLEXPRESS;Database=NEXORA;Integrated Security=True"));
        Assert.False(DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase("not a connection string"));
    }

    [Fact]
    public void Parse_ProvisioningCommandWithArguments_RequiresInteractiveStandaloneInvocation()
    {
        Assert.Throws<ArgumentException>(() =>
            ProvisioningCommandArguments.Parse(["--provision-admin", "admin@example.test"]));
    }
}
