using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Domain.Security;
using NEXORA.Web.Controllers;

namespace NEXORA.Tests;

public sealed class CommerceAuthorizationTests
{
    [Theory]
    [InlineData(typeof(CartController))]
    [InlineData(typeof(CheckoutController))]
    [InlineData(typeof(OrdersController))]
    public void CommerceControllers_RequireCustomerRole(Type controllerType)
    {
        var authorize = Assert.Single(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(RoleNames.Customer, authorize.Roles);
    }

    [Theory]
    [InlineData(typeof(CartController), nameof(CartController.Add))]
    [InlineData(typeof(CartController), nameof(CartController.Update))]
    [InlineData(typeof(CartController), nameof(CartController.Remove))]
    [InlineData(typeof(CheckoutController), nameof(CheckoutController.Place))]
    [InlineData(typeof(OrdersController), nameof(OrdersController.Cancel))]
    public void CommerceMutationActions_RequireAntiforgery(Type controllerType, string actionName)
    {
        var action = controllerType.GetMethod(actionName);

        Assert.NotNull(action);
        Assert.Contains(action!.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true),
            attribute => attribute is ValidateAntiForgeryTokenAttribute);
    }

    [Fact]
    public void CustomerOrderEndpoints_AreDistinctFromAdminManagementArea()
    {
        Assert.Null(typeof(OrdersController).GetCustomAttributes(typeof(AreaAttribute), inherit: true).SingleOrDefault());
    }
}
