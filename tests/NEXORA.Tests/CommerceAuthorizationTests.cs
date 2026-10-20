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

    [Fact]
    public void CancellationReviewEndpoints_RequireStaffRoleAndAntiforgery()
    {
        var sales = typeof(NEXORA.Web.Areas.Sales.Controllers.OrdersController);
        var admin = typeof(NEXORA.Web.Areas.Admin.Controllers.OrdersController);
        Assert.Equal(RoleNames.Sales, sales.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Roles);
        Assert.Equal(RoleNames.Admin, admin.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Roles);

        foreach (var controller in new[] { sales, admin })
        {
            var action = controller.GetMethod("ReviewCancellation");
            Assert.NotNull(action);
            Assert.NotNull(action!.GetCustomAttributes(typeof(HttpPostAttribute), true).SingleOrDefault());
            Assert.NotNull(action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).SingleOrDefault());
        }
    }

    [Theory]
    [InlineData(typeof(CartController), nameof(CartController.Add))]
    [InlineData(typeof(CartController), nameof(CartController.Update))]
    [InlineData(typeof(CartController), nameof(CartController.Remove))]
    [InlineData(typeof(CheckoutController), nameof(CheckoutController.Place))]
    [InlineData(typeof(OrdersController), nameof(OrdersController.Cancel))]
    [InlineData(typeof(OrdersController), nameof(OrdersController.RequestCancellation))]
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
