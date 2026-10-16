using System.Reflection;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Domain.Security;
using NEXORA.Web.Areas.Admin.Controllers;
using NEXORA.Web.Areas.Admin.Models;

namespace NEXORA.Tests;

public sealed class ConsultationAdminAuthorizationTests
{
    [Fact]
    public void ConsultationManagementControllerRequiresAdminRole()
    {
        var authorization = typeof(ConsultationController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal(RoleNames.Admin, authorization!.Roles);
        Assert.Equal("Admin", typeof(ConsultationController).GetCustomAttribute<AreaAttribute>()?.RouteValue);
    }

    [Theory]
    [InlineData("CreateUsageNeed")]
    [InlineData("EditUsageNeed")]
    [InlineData("ToggleUsageNeed")]
    [InlineData("CreateRule")]
    [InlineData("EditRule")]
    [InlineData("ToggleRule")]
    public void EveryConsultationMutationRequiresPostAndAntiforgery(string actionName)
    {
        var method = typeof(ConsultationController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(item => item.Name == actionName && item.GetCustomAttribute<HttpPostAttribute>() is not null);

        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void RecommendationRuleWeightRangeIsCultureIndependent()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("vi-VN");
            var attribute = new InvariantDecimalRangeAttribute("0", "99999.9999");
            var context = new ValidationContext(new object());

            Assert.Equal(ValidationResult.Success, attribute.GetValidationResult(99_999.9999m, context));
            Assert.NotEqual(ValidationResult.Success, attribute.GetValidationResult(100_000m, context));
            Assert.NotEqual(ValidationResult.Success, attribute.GetValidationResult(-0.0001m, context));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
