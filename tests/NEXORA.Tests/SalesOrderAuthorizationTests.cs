using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Domain.Security;

namespace NEXORA.Tests;

public sealed class SalesOrderAuthorizationTests
{
    [Fact]
    public void SalesOrderEndpoints_RequireSalesRole_AndProtectMutationsWithAntiforgery()
    {
        var controller = typeof(NEXORA.Web.Areas.Sales.Controllers.OrdersController);

        Assert.Equal("Sales", controller.GetCustomAttribute<AreaAttribute>()?.RouteValue);
        Assert.Equal(RoleNames.Sales, controller.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
        AssertMutationIsProtected(controller, "Confirm");
        AssertMutationIsProtected(controller, "Handoff");
        AssertMutationIsProtected(controller, "MarkShipping");
        AssertMutationIsProtected(controller, "Complete");
        AssertMutationIsProtected(controller, "ReviewCancellation");
    }

    [Fact]
    public void AdminOrderEndpoints_RequireAdminRole_AndExposeNoWarehouseOrShippingMutations()
    {
        var controller = typeof(NEXORA.Web.Areas.Admin.Controllers.OrdersController);

        Assert.Equal("Admin", controller.GetCustomAttribute<AreaAttribute>()?.RouteValue);
        Assert.Equal(RoleNames.Admin, controller.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
        AssertMutationIsProtected(controller, "Confirm");
        AssertMutationIsProtected(controller, "ReviewCancellation");
        Assert.Null(controller.GetMethod("Handoff"));
        Assert.Null(controller.GetMethod("MarkShipping"));
        Assert.Null(controller.GetMethod("Complete"));
    }

    private static void AssertMutationIsProtected(Type controller, string actionName)
    {
        var action = controller.GetMethod(actionName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
