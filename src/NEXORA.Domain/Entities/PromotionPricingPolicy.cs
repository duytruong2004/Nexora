namespace NEXORA.Domain.Entities;

public sealed record PromotionOffer(int Id, string Name, string DiscountType, decimal DiscountValue);

public sealed record PromotionDiscount(int? PromotionId, string? PromotionName, decimal Amount);

public static class PromotionPricingPolicy
{
    public static PromotionDiscount SelectBest(
        decimal unitPrice,
        int quantity,
        IEnumerable<PromotionOffer> eligibleOffers)
    {
        ArgumentNullException.ThrowIfNull(eligibleOffers);
        if (unitPrice < 0 || quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "A non-negative price and positive quantity are required.");
        }

        var lineAmount = Round(unitPrice * quantity);
        return eligibleOffers
            .Select(offer => new
            {
                Offer = offer,
                Amount = Calculate(offer, lineAmount)
            })
            .Where(candidate => candidate.Amount > 0)
            .OrderByDescending(candidate => candidate.Amount)
            .ThenBy(candidate => candidate.Offer.Id)
            .Select(candidate => new PromotionDiscount(
                candidate.Offer.Id,
                candidate.Offer.Name,
                candidate.Amount))
            .FirstOrDefault() ?? new PromotionDiscount(null, null, 0m);
    }

    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static decimal Calculate(PromotionOffer offer, decimal lineAmount)
    {
        if (offer.DiscountValue <= 0)
        {
            return 0m;
        }

        var proposed = offer.DiscountType switch
        {
            PromotionDiscountTypes.Percentage when offer.DiscountValue <= 100m =>
                Round(lineAmount * offer.DiscountValue / 100m),
            PromotionDiscountTypes.FixedAmount => Round(offer.DiscountValue),
            _ => 0m
        };

        return Math.Min(lineAmount, Math.Max(0m, proposed));
    }
}
