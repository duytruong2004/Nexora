using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Domain.Security;

namespace NEXORA.Tests;

public sealed class Phase6AuthorizationTests
{
    [Fact]
    public void CustomerEngagementEndpoints_RequireCustomerRole()
    {
        foreach (var controllerType in new[]
                 {
                     typeof(NEXORA.Web.Controllers.FavoritesController),
                     typeof(NEXORA.Web.Controllers.ReviewsController)
                 })
        {
            var authorize = Assert.Single(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
            Assert.Equal(RoleNames.Customer, authorize.Roles);
        }

        AssertProtectedPost(typeof(NEXORA.Web.Controllers.FavoritesController), nameof(NEXORA.Web.Controllers.FavoritesController.Add));
        AssertProtectedPost(typeof(NEXORA.Web.Controllers.FavoritesController), nameof(NEXORA.Web.Controllers.FavoritesController.Remove));
        AssertProtectedPost(typeof(NEXORA.Web.Controllers.ReviewsController), nameof(NEXORA.Web.Controllers.ReviewsController.Submit));
    }

    [Fact]
    public void ReviewModerationAndReports_AreAdminOnlyAndReadOnlyReportsHaveNoMutationActions()
    {
        foreach (var controllerType in new[]
                 {
                     typeof(NEXORA.Web.Areas.Admin.Controllers.ReviewsController),
                     typeof(NEXORA.Web.Areas.Admin.Controllers.ReportsController)
                 })
        {
            var authorize = Assert.Single(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
            Assert.Equal(RoleNames.Admin, authorize.Roles);
        }

        AssertProtectedPost(typeof(NEXORA.Web.Areas.Admin.Controllers.ReviewsController), "Transition");
        Assert.All(typeof(NEXORA.Web.Areas.Admin.Controllers.ReportsController).GetMethods()
                .Where(method => method.DeclaringType == typeof(NEXORA.Web.Areas.Admin.Controllers.ReportsController) && method.IsPublic),
            method => Assert.NotNull(method.GetCustomAttributes(typeof(HttpGetAttribute), true).SingleOrDefault()));
    }

    private static void AssertProtectedPost(Type controller, string actionName)
    {
        var action = controller.GetMethod(actionName);
        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttributes(typeof(HttpPostAttribute), true).SingleOrDefault());
        Assert.NotNull(action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).SingleOrDefault());
    }
}
