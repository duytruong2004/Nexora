using NEXORA.Domain.Entities;

namespace NEXORA.Application.Catalog;

public sealed record CategoryInput(int? Id, string Name, string? Description, int? ParentId);

public sealed record BrandInput(int? Id, string Name, string? Description, string? LogoUrl);

public enum CatalogRemovalOutcome
{
    NotFound,
    Deleted,
    Deactivated
}

public sealed class CatalogValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public interface ICategoryBrandManagementService
{
    Task<IReadOnlyList<Category>> SearchCategoriesAsync(string? search, CancellationToken cancellationToken = default);
    Task<Category?> GetCategoryAsync(int id, CancellationToken cancellationToken = default);
    Task<int> SaveCategoryAsync(CategoryInput input, CancellationToken cancellationToken = default);
    Task<CatalogRemovalOutcome> RemoveOrDeactivateCategoryAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Brand>> SearchBrandsAsync(string? search, CancellationToken cancellationToken = default);
    Task<Brand?> GetBrandAsync(int id, CancellationToken cancellationToken = default);
    Task<int> SaveBrandAsync(BrandInput input, CancellationToken cancellationToken = default);
    Task<CatalogRemovalOutcome> RemoveOrDeactivateBrandAsync(int id, CancellationToken cancellationToken = default);
}
