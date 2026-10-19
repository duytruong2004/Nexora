using System.ComponentModel.DataAnnotations;
using System.Globalization;
using NEXORA.Domain.Entities;
using NEXORA.Web.Areas.Admin.Models;

namespace NEXORA.Tests;

public sealed class PromotionFormViewModelValidationTests
{
    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void DecimalRangeUsesInvariantBoundsAndAcceptsSqlDecimalMaximum(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            var errors = Validate(Create(PromotionDiscountTypes.FixedAmount, 9_999_999_999_999_999.99m));

            Assert.DoesNotContain(errors, error => error.MemberNames.Contains(nameof(PromotionFormViewModel.DiscountValue)));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Theory]
    [InlineData("PERCENTAGE", 0, true)]
    [InlineData("PERCENTAGE", 100, true)]
    [InlineData("PERCENTAGE", 10.25, true)]
    [InlineData("FIXED_AMOUNT", 0, true)]
    [InlineData("FIXED_AMOUNT", 250000.50, true)]
    [InlineData("FIXED_AMOUNT", -0.01, false)]
    [InlineData("FIXED_AMOUNT", 10000000000000000, false)]
    [InlineData("PERCENTAGE", 100.01, false)]
    [InlineData("FIXED_AMOUNT", 1.001, false)]
    [InlineData("UNKNOWN", 10, false)]
    public void DiscountValueAndTypeAreValidated(string discountType, decimal value, bool expectedValid)
    {
        var errors = Validate(Create(discountType, value));

        Assert.Equal(expectedValid, errors.Count == 0);
    }

    private static List<ValidationResult> Validate(PromotionFormViewModel model)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), errors, validateAllProperties: true);
        return errors;
    }

    private static PromotionFormViewModel Create(string discountType, decimal value) => new()
    {
        Name = "QA promotion validation",
        DiscountType = discountType,
        DiscountValue = value,
        StartDate = new DateTime(2026, 10, 16, 10, 0, 0),
        EndDate = new DateTime(2026, 10, 23, 10, 0, 0),
        SelectedProductIds = [1]
    };
}
