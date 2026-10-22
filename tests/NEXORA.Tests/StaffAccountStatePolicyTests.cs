using NEXORA.Application.Identity;
using NEXORA.Domain.Security;

namespace NEXORA.Tests;

public sealed class StaffAccountStatePolicyTests
{
    [Theory]
    [InlineData(RoleNames.Sales)]
    [InlineData(RoleNames.Warehouse)]
    public void PendingStaffInvitation_IsInactiveUnconfirmedAndHasNoPassword(string role)
    {
        Assert.Equal(AccountLifecycleState.PendingInvitation,
            StaffAccountStatePolicy.Classify(false, false, false, [role]));
    }

    [Theory]
    [InlineData(RoleNames.Sales)]
    [InlineData(RoleNames.Warehouse)]
    public void ActivatedStaff_IsActiveEvenWhenLegacyEmailConfirmationIsFalse(string role)
    {
        Assert.Equal(AccountLifecycleState.Active,
            StaffAccountStatePolicy.Classify(true, false, true, [role]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SuspendedStaff_HasPasswordAndCannotBeTreatedAsPendingInvitation(bool emailConfirmed)
    {
        Assert.Equal(AccountLifecycleState.Suspended,
            StaffAccountStatePolicy.Classify(false, emailConfirmed, true, [RoleNames.Sales]));
    }

    [Fact]
    public void AccountWithMultipleRoles_IsInconsistentAndMustNotBeAutomaticallyRewritten()
    {
        Assert.Equal(AccountLifecycleState.Inconsistent,
            StaffAccountStatePolicy.Classify(true, true, true, [RoleNames.Customer, RoleNames.Admin]));
    }

    [Fact]
    public void InactiveAccountWithoutPasswordAndWithNonStaffRole_IsNotAnInvitation()
    {
        Assert.Equal(AccountLifecycleState.Inconsistent,
            StaffAccountStatePolicy.Classify(false, false, false, [RoleNames.Customer]));
    }

    [Fact]
    public void UnknownPersistedRole_IsInconsistent()
    {
        Assert.Equal(AccountLifecycleState.Inconsistent,
            StaffAccountStatePolicy.Classify(true, true, true, ["UnexpectedRole"]));
    }

    [Theory]
    [InlineData(RoleNames.Sales, true)]
    [InlineData(RoleNames.Warehouse, true)]
    [InlineData(RoleNames.Customer, false)]
    [InlineData(RoleNames.Admin, false)]
    [InlineData("Unknown", false)]
    public void OnlyApprovedStaffRolesAreAssignable(string role, bool expected)
    {
        Assert.Equal(expected, StaffAccountStatePolicy.IsStaffRole(role));
    }
}
