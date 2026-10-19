using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Commerce;

public sealed class AdminPromotionManagementService(AppDbContext db) : IAdminPromotionManagementService
{
    public async Task<IReadOnlyList<PromotionAdminRow>> GetPromotionsAsync(CancellationToken cancellationToken = default)
    {
        var promotions = await db.Promotions.AsNoTracking()
            .Include(item => item.Products).ThenInclude(link => link.Product)
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.StartDate)
            .ToListAsync(cancellationToken);
        return promotions.Select(item => new PromotionAdminRow(
            item.Id,
            item.Name,
            item.DiscountType,
            item.DiscountValue,
            item.StartDate,
            item.EndDate,
            item.IsActive,
            item.Products.Count,
            item.Products.Select(link => link.Product.Name).OrderBy(name => name).ToArray())).ToArray();
    }

    public async Task<IReadOnlyList<PromotionProductOption>> GetProductsAsync(CancellationToken cancellationToken = default) =>
        await db.Products.AsNoTracking()
            .Where(item => item.Status == ProductStatuses.Active && item.Category.IsActive && item.Brand.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new PromotionProductOption(item.Id, item.Name, item.Sku))
            .ToListAsync(cancellationToken);

    public async Task<PromotionAdminForm?> GetPromotionAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) return null;
        var item = await db.Promotions.AsNoTracking()
            .Include(promotion => promotion.Products)
            .SingleOrDefaultAsync(promotion => promotion.Id == id, cancellationToken);
        return item is null
            ? null
            : new PromotionAdminForm(item.Id, item.Name, item.Description, item.DiscountType,
                item.DiscountValue, item.StartDate, item.EndDate, item.IsActive,
                item.Products.Select(link => link.ProductId).OrderBy(productId => productId).ToArray());
    }

    public async Task<int> SaveAsync(PromotionAdminForm input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        var productIds = input.ProductIds.Distinct().ToArray();
        var foundProductIds = await db.Products.AsNoTracking()
            .Where(product => productIds.Contains(product.Id) && product.Status == ProductStatuses.Active &&
                              product.Category.IsActive && product.Brand.IsActive)
            .Select(product => product.Id)
            .ToArrayAsync(cancellationToken);
        if (foundProductIds.Length != productIds.Length)
        {
            throw new PromotionValidationException(nameof(input.ProductIds), "Có sản phẩm không tồn tại hoặc đã ngừng kinh doanh.");
        }

        var startUtc = LocalInputToUtc(input.StartDate);
        var endUtc = LocalInputToUtc(input.EndDate);
        Promotion promotion;
        if (input.Id is null)
        {
            promotion = new Promotion();
            db.Promotions.Add(promotion);
        }
        else
        {
            promotion = await db.Promotions
                .Include(item => item.Products)
                .SingleOrDefaultAsync(item => item.Id == input.Id.Value, cancellationToken)
                ?? throw new PromotionValidationException(nameof(input.Id), "Không tìm thấy chương trình khuyến mại.");

            var currentLinks = promotion.Products.ToDictionary(link => link.ProductId);
            foreach (var link in promotion.Products.Where(link => !productIds.Contains(link.ProductId)).ToArray())
            {
                db.PromotionProducts.Remove(link);
            }
            foreach (var productId in productIds.Where(productId => !currentLinks.ContainsKey(productId)))
            {
                promotion.Products.Add(new PromotionProduct { ProductId = productId, PromotionId = promotion.Id });
            }
        }

        promotion.Name = input.Name.Trim();
        promotion.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        promotion.DiscountType = input.DiscountType;
        promotion.DiscountValue = decimal.Round(input.DiscountValue, 2, MidpointRounding.AwayFromZero);
        promotion.StartDate = startUtc;
        promotion.EndDate = endUtc;
        promotion.IsActive = input.IsActive;
        if (input.Id is null)
        {
            foreach (var productId in productIds)
            {
                promotion.Products.Add(new PromotionProduct { ProductId = productId, Promotion = promotion });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return promotion.Id;
    }

    public async Task<bool> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default) =>
        await db.Promotions.Where(item => item.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, isActive), cancellationToken) == 1;

    private static void Validate(PromotionAdminForm input)
    {
        if (input.Id is <= 0) throw new PromotionValidationException(nameof(input.Id), "Mã khuyến mại không hợp lệ.");
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200)
            throw new PromotionValidationException(nameof(input.Name), "Tên bắt buộc và tối đa 200 ký tự.");
        if (input.Description?.Length > 2000)
            throw new PromotionValidationException(nameof(input.Description), "Mô tả tối đa 2.000 ký tự.");
        if (input.DiscountType is not PromotionDiscountTypes.Percentage and not PromotionDiscountTypes.FixedAmount)
            throw new PromotionValidationException(nameof(input.DiscountType), "Loại khuyến mại không hợp lệ.");
        if (input.DiscountValue < 0 || decimal.Round(input.DiscountValue, 2) != input.DiscountValue)
            throw new PromotionValidationException(nameof(input.DiscountValue), "Mức giảm phải không âm và tối đa hai chữ số thập phân.");
        if (input.DiscountType == PromotionDiscountTypes.Percentage && input.DiscountValue > 100)
            throw new PromotionValidationException(nameof(input.DiscountValue), "Phần trăm giảm phải từ 0 đến 100.");
        if (input.StartDate == default || input.EndDate == default || LocalInputToUtc(input.EndDate) < LocalInputToUtc(input.StartDate))
            throw new PromotionValidationException(nameof(input.EndDate), "Thời gian kết thúc phải bằng hoặc sau thời gian bắt đầu.");
        if (input.ProductIds.Count == 0 || input.ProductIds.Any(id => id <= 0))
            throw new PromotionValidationException(nameof(input.ProductIds), "Chọn ít nhất một sản phẩm hợp lệ.");
    }

    private static DateTime LocalInputToUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();
}
