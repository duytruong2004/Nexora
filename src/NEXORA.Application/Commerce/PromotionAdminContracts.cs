namespace NEXORA.Application.Commerce;

public sealed record PromotionProductOption(int Id, string Name, string Sku);

public sealed record PromotionAdminRow(
    int Id,
    string Name,
    string DiscountType,
    decimal DiscountValue,
    DateTime StartDate,
    DateTime EndDate,
    bool IsActive,
    int ProductCount,
    IReadOnlyList<string> ProductNames);

public sealed record PromotionAdminForm(
    int? Id,
    string Name,
    string? Description,
    string DiscountType,
    decimal DiscountValue,
    DateTime StartDate,
    DateTime EndDate,
    bool IsActive,
    IReadOnlyList<int> ProductIds);

public sealed class PromotionValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public interface IAdminPromotionManagementService
{
    Task<IReadOnlyList<PromotionAdminRow>> GetPromotionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PromotionProductOption>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<PromotionAdminForm?> GetPromotionAsync(int id, CancellationToken cancellationToken = default);
    Task<int> SaveAsync(PromotionAdminForm input, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default);
}
