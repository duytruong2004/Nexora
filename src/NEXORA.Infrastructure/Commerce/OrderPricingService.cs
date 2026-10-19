using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Commerce;

public sealed class OrderPricingService(AppDbContext db) : IOrderPricingService
{
    public async Task<CheckoutQuote> GetCurrentQuoteAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        var now = DateTime.UtcNow;
        var cartLines = await db.Carts.AsNoTracking()
            .Where(cart => cart.UserId == userId)
            .SelectMany(cart => cart.Items)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                CartItemId = item.Id,
                item.ProductId,
                item.Product.Name,
                item.Product.Sku,
                item.Quantity,
                item.Product.Price,
                item.Product.DiscountPrice,
                IsProductActive = item.Product.Status == ProductStatuses.Active &&
                                  item.Product.Category.IsActive && item.Product.Brand.IsActive,
                Available = item.Product.Inventory == null
                    ? 0
                    : item.Product.Inventory.QuantityInStock - item.Product.Inventory.ReservedQuantity,
                ImageUrl = item.Product.Images.Where(image => image.IsPrimary)
                    .OrderBy(image => image.SortOrder).Select(image => image.ImageUrl).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        if (cartLines.Count == 0)
        {
            return CreateQuote([], now);
        }

        var productIds = cartLines.Select(line => line.ProductId).Distinct().ToArray();
        var offers = await db.PromotionProducts.AsNoTracking()
            .Where(link => productIds.Contains(link.ProductId) && link.Promotion.IsActive &&
                           link.Promotion.StartDate <= now && link.Promotion.EndDate >= now)
            .Select(link => new
            {
                link.ProductId,
                Offer = new PromotionOffer(
                    link.PromotionId,
                    link.Promotion.Name,
                    link.Promotion.DiscountType,
                    link.Promotion.DiscountValue)
            })
            .ToListAsync(cancellationToken);
        var offersByProduct = offers.GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Offer).ToArray());

        var lines = cartLines.Select(line =>
        {
            var unitPrice = line.DiscountPrice is >= 0 && line.DiscountPrice <= line.Price
                ? line.DiscountPrice.Value
                : line.Price;
            var discount = PromotionPricingPolicy.SelectBest(
                unitPrice,
                line.Quantity,
                offersByProduct.GetValueOrDefault(line.ProductId, []));
            return new CheckoutQuoteLine(
                line.CartItemId,
                line.ProductId,
                line.Name,
                line.Sku,
                line.ImageUrl,
                unitPrice,
                discount.Amount,
                discount.PromotionName,
                line.Quantity,
                Math.Max(0, line.Available),
                line.IsProductActive);
        }).ToArray();

        return CreateQuote(lines, now);
    }

    private static CheckoutQuote CreateQuote(IReadOnlyList<CheckoutQuoteLine> lines, DateTime createdAtUtc)
    {
        var canonical = lines.Select(line => new
        {
            line.CartItemId,
            line.ProductId,
            line.ProductName,
            line.Sku,
            line.UnitPrice,
            line.DiscountAmount,
            line.PromotionName,
            line.Quantity
        });
        var serialized = JsonSerializer.Serialize(canonical);
        var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(serialized)));
        return new CheckoutQuote(lines, createdAtUtc, fingerprint);
    }
}
