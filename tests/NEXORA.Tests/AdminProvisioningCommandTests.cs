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
        Assert.Empty(parsed.HostArguments);
    }

    [Fact]
    public void Parse_ProvisioningCommandWithArguments_RequiresInteractiveStandaloneInvocation()
    {
        Assert.Throws<ArgumentException>(() =>
            ProvisioningCommandArguments.Parse(["--provision-admin", "admin@example.test"]));
    }
}
