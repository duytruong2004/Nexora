using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using NEXORA.Web.Areas.Admin.Controllers;

namespace NEXORA.Tests;

public sealed class CatalogAdminAuthorizationTests
{
    [Theory]
    [InlineData(typeof(CategoriesController))]
    [InlineData(typeof(BrandsController))]
    [InlineData(typeof(ProductsController))]
    [InlineData(typeof(SpecificationsController))]
    [InlineData(typeof(ProductSpecificationsController))]
    public void CatalogAdminControllers_RequireAdminRole(Type controllerType)
    {
        var authorization = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal("Admin", authorization.Roles);
    }

    [Theory]
    [InlineData(typeof(CategoriesController))]
    [InlineData(typeof(BrandsController))]
    [InlineData(typeof(ProductsController))]
    [InlineData(typeof(SpecificationsController))]
    [InlineData(typeof(ProductSpecificationsController))]
    public void MutatingActions_RequirePostAndAntiforgery(Type controllerType)
    {
        var actions = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>() is not null)
            .ToList();

        Assert.NotEmpty(actions);
        Assert.All(actions, method =>
            Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>()));
    }
}
