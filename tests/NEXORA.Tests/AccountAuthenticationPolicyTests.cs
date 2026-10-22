using NEXORA.Application.Identity;
using NEXORA.Domain.Security;

namespace NEXORA.Tests;

public sealed class AccountAuthenticationPolicyTests
{
    [Theory]
    [InlineData(RoleNames.Customer)]
    [InlineData(RoleNames.Sales)]
    [InlineData(RoleNames.Warehouse)]
    [InlineData(RoleNames.Admin)]
    public void ActiveAccountWithOneKnownRole_CanIssueCookie(string role)
    {
        Assert.True(AccountAuthenticationPolicy.CanIssueCookie(true, [role]));
    }

    [Fact]
    public void AccountWithoutRole_CannotIssueCookie()
    {
        Assert.False(AccountAuthenticationPolicy.CanIssueCookie(true, []));
    }

    [Theory]
    [InlineData(RoleNames.Admin, RoleNames.Customer)]
    [InlineData(RoleNames.Sales, RoleNames.Warehouse)]
    public void MultiRoleAccount_CannotIssueCookie(string firstRole, string secondRole)
    {
        Assert.False(AccountAuthenticationPolicy.CanIssueCookie(true, [firstRole, secondRole]));
    }

    [Fact]
    public void AccountWithUnknownRole_CannotIssueCookie()
    {
        Assert.False(AccountAuthenticationPolicy.CanIssueCookie(true, ["UnexpectedRole"]));
    }

    [Fact]
    public void InactiveAccount_CannotIssueCookie()
    {
        Assert.False(AccountAuthenticationPolicy.CanIssueCookie(false, [RoleNames.Admin]));
    }

    [Theory]
    [InlineData(RoleNames.Sales)]
    [InlineData(RoleNames.Warehouse)]
    public void PendingInvitation_CannotIssueCookie(string role)
    {
        // Staff invitations are inactive and cannot complete password verification before activation.
        Assert.False(AccountAuthenticationPolicy.CanIssueCookie(false, [role]));
    }

    [Theory]
    [InlineData(RoleNames.Customer)]
    [InlineData(RoleNames.Sales)]
    [InlineData(RoleNames.Warehouse)]
    [InlineData(RoleNames.Admin)]
    public void CookieWithMatchingSingleKnownRole_IsCurrent(string role)
    {
        Assert.True(AccountAuthenticationPolicy.HasCurrentSingleRole(true, [role], [role]));
    }

    [Fact]
    public void MatchingMultiRoleCookieAndDatabaseState_AreRejected()
    {
        Assert.False(AccountAuthenticationPolicy.HasCurrentSingleRole(
            true,
            [RoleNames.Admin, RoleNames.Customer],
            [RoleNames.Admin, RoleNames.Customer]));
    }

    [Theory]
    [InlineData(RoleNames.Admin, RoleNames.Customer)]
    [InlineData(RoleNames.Sales, RoleNames.Warehouse)]
    public void CookieAndDatabaseRoleMismatch_IsRejected(string cookieRole, string persistedRole)
    {
        Assert.False(AccountAuthenticationPolicy.HasCurrentSingleRole(
            true, [persistedRole], [cookieRole]));
    }

    [Fact]
    public void DuplicateRoleClaims_AreRejectedRatherThanDeduplicated()
    {
        Assert.False(AccountAuthenticationPolicy.HasCurrentSingleRole(
            true,
            [RoleNames.Admin],
            [RoleNames.Admin, RoleNames.Admin]));
    }

    [Fact]
    public void UnknownRoleInCookieOrDatabase_IsRejected()
    {
        Assert.False(AccountAuthenticationPolicy.HasCurrentSingleRole(
            true, ["UnexpectedRole"], ["UnexpectedRole"]));
    }

    [Fact]
    public void InactiveAccountCookie_IsRejected()
    {
        Assert.False(AccountAuthenticationPolicy.HasCurrentSingleRole(
            false, [RoleNames.Admin], [RoleNames.Admin]));
    }
}
