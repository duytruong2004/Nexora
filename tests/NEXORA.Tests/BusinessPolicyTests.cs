using NEXORA.Domain.Entities;

namespace NEXORA.Tests;

public sealed class BusinessPolicyTests
{
    [Theory]
    [InlineData(OrderStatuses.Preparing, null, true)]
    [InlineData(OrderStatuses.Preparing, OrderCancellationRequestStatuses.Rejected, true)]
    [InlineData(OrderStatuses.Preparing, OrderCancellationRequestStatuses.Pending, false)]
    [InlineData(OrderStatuses.Preparing, OrderCancellationRequestStatuses.Approved, false)]
    [InlineData(OrderStatuses.Preparing, "UNKNOWN", false)]
    [InlineData(OrderStatuses.Exported, null, false)]
    public void OrderCancellationRules_ExportRequiresPreparingAndNoUnresolvedCancellation(
        string orderStatus,
        string? requestStatus,
        bool expected) =>
        Assert.Equal(expected, OrderCancellationRules.CanExport(orderStatus, requestStatus));

    [Theory]
    [InlineData("NUMBER", "GTE", "16", true)]
    [InlineData("NUMBER", "CONTAINS", "16", false)]
    [InlineData("NUMBER", "LTE", "16,5", false)]
    [InlineData("TEXT", "CONTAINS", "OLED", true)]
    [InlineData("TEXT", "GT", "OLED", false)]
    [InlineData("ENUM", "EQ", "AMOLED", true)]
    [InlineData("BOOLEAN", "NE", "false", true)]
    [InlineData("BOOLEAN", "EQ", "yes", false)]
    [InlineData("UNREGISTERED", "EQ", "value", false)]
    public void RecommendationRulePolicy_ValidatesOperatorAndThresholdForDynamicType(
        string dataType,
        string operation,
        string threshold,
        bool expectedValid)
    {
        var rule = new RecommendationRule
        {
            Operator = operation,
            ThresholdValue = threshold,
            Weight = 1.25m
        };
        var specification = new Specification { DataType = dataType };

        var error = RecommendationRulePolicy.Validate(rule, specification);

        Assert.Equal(expectedValid, error is null);
    }

    [Fact]
    public void RecommendationRulePolicy_RejectsWeightOutsideConfiguredDecimalRange()
    {
        foreach (var weight in new[] { -0.0001m, 100_000m })
        {
            var rule = new RecommendationRule
            {
                Operator = RecommendationRuleOperators.GreaterThanOrEqual,
                ThresholdValue = "16",
                Weight = weight
            };

            Assert.NotNull(RecommendationRulePolicy.Validate(
                rule, new Specification { DataType = SpecificationDataTypes.Number }));
        }
    }

    [Fact]
    public void RecommendationRulePolicy_AllowsZeroWeightAsSpecifiedByRankingFormula()
    {
        var rule = new RecommendationRule
        {
            Operator = RecommendationRuleOperators.GreaterThanOrEqual,
            ThresholdValue = "16",
            Weight = 0
        };

        Assert.Null(RecommendationRulePolicy.Validate(
            rule, new Specification { DataType = SpecificationDataTypes.Number }));
    }

    [Fact]
    public void ReviewEligibilityPolicy_RequiresOrderOwnerMatchingDetailAndCompletedOrder()
    {
        var completedOrder = new Order { Id = 17, UserId = 42, Status = OrderStatuses.Completed };
        var detail = new OrderDetail { Id = 9, OrderId = 17, ProductId = 33 };
        var review = new Review { UserId = 42, ProductId = 33, OrderId = 17, OrderDetailId = 9 };

        Assert.Null(ReviewEligibilityPolicy.Validate(review, completedOrder, detail));
        Assert.NotNull(ReviewEligibilityPolicy.Validate(
            new Review { UserId = 41, ProductId = 33, OrderId = 17, OrderDetailId = 9 },
            new Order { Id = 17, UserId = 100, Status = OrderStatuses.Completed }, detail));
        Assert.NotNull(ReviewEligibilityPolicy.Validate(
            review,
            new Order { Id = 17, UserId = 42, Status = OrderStatuses.Pending }, detail));
        Assert.NotNull(ReviewEligibilityPolicy.Validate(
            new Review { UserId = 42, ProductId = 34, OrderId = 17, OrderDetailId = 9 },
            completedOrder, detail));
    }

    [Fact]
    public void Promotion_IsApplicableOnlyWhenActiveAndWithinConfiguredPeriod()
    {
        var promotion = new Promotion
        {
            IsActive = true,
            StartDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc)
        };

        Assert.True(promotion.IsApplicableAt(new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(promotion.IsApplicableAt(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)));
        promotion.IsActive = false;
        Assert.False(promotion.IsApplicableAt(new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void PromotionPricingPolicy_UsesLargestSingleEligibleDiscount()
    {
        var result = PromotionPricingPolicy.SelectBest(1_000m, 2,
        [
            new PromotionOffer(12, "Phần trăm", PromotionDiscountTypes.Percentage, 10m),
            new PromotionOffer(8, "Số tiền", PromotionDiscountTypes.FixedAmount, 350m),
            new PromotionOffer(2, "Không hợp lệ", PromotionDiscountTypes.Percentage, 101m)
        ]);

        Assert.Equal(8, result.PromotionId);
        Assert.Equal("Số tiền", result.PromotionName);
        Assert.Equal(350m, result.Amount);
    }

    [Fact]
    public void PromotionPricingPolicy_FixedAmountIsAppliedOncePerLineAndCappedAtLineTotal()
    {
        var regular = PromotionPricingPolicy.SelectBest(1_000m, 4,
            [new PromotionOffer(1, "Giảm cố định", PromotionDiscountTypes.FixedAmount, 250m)]);
        var capped = PromotionPricingPolicy.SelectBest(100m, 1,
            [new PromotionOffer(1, "Giảm lớn", PromotionDiscountTypes.FixedAmount, 500m)]);

        Assert.Equal(250m, regular.Amount);
        Assert.Equal(100m, capped.Amount);
    }

    [Fact]
    public void PromotionPricingPolicy_RoundsPercentAndBreaksTiesDeterministically()
    {
        var result = PromotionPricingPolicy.SelectBest(1.05m, 1,
        [
            new PromotionOffer(9, "Later id", PromotionDiscountTypes.Percentage, 10m),
            new PromotionOffer(3, "Earlier id", PromotionDiscountTypes.Percentage, 10m)
        ]);

        Assert.Equal(3, result.PromotionId);
        Assert.Equal(0.11m, result.Amount);
    }
}
