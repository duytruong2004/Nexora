namespace NEXORA.Application.Catalog;

public sealed record SpecificationInput(int? Id, string Name, string? Unit, string DataType);

public sealed record SpecificationAdminRow(int Id, string Name, string? Unit, string DataType, bool IsActive);

public sealed record CategorySpecificationInput(
    int CategoryId,
    int SpecificationId,
    bool IsFilterable,
    bool IsRequired,
    int SortOrder);

public sealed record CategorySpecificationAdminRow(
    int SpecificationId,
    string Name,
    string? Unit,
    string DataType,
    bool IsFilterable,
    bool IsRequired,
    int SortOrder);

public sealed record CategorySpecificationConfigurationResult(
    int CategoryId,
    string CategoryName,
    IReadOnlyList<CategorySpecificationAdminRow> AssignedSpecifications,
    IReadOnlyList<SpecificationAdminRow> AvailableSpecifications);

public sealed record ProductSpecificationValueInput(int SpecificationId, string? Value, string? DisplayValue);

public sealed record ProductSpecificationEditorValue(
    int SpecificationId,
    string Name,
    string? Unit,
    string DataType,
    bool IsRequired,
    string? Value,
    string? DisplayValue,
    IReadOnlyList<string> EnumOptions);

public sealed record ProductSpecificationEditor(
    int ProductId,
    string ProductName,
    string CategoryName,
    IReadOnlyList<ProductSpecificationEditorValue> Values);

public interface ISpecificationManagementService
{
    Task<IReadOnlyList<SpecificationAdminRow>> SearchAsync(string? search, CancellationToken cancellationToken = default);
    Task<SpecificationAdminRow?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<int> SaveAsync(SpecificationInput input, CancellationToken cancellationToken = default);
    Task<bool> DeactivateAsync(int specificationId, CancellationToken cancellationToken = default);
    Task<CategorySpecificationConfigurationResult?> GetCategoryConfigurationAsync(int categoryId, CancellationToken cancellationToken = default);
    Task SaveCategorySpecificationAsync(CategorySpecificationInput input, CancellationToken cancellationToken = default);
    Task<bool> RemoveCategorySpecificationAsync(int categoryId, int specificationId, CancellationToken cancellationToken = default);
}

public interface IProductSpecificationManagementService
{
    Task<ProductSpecificationEditor?> GetEditorAsync(int productId, CancellationToken cancellationToken = default);
    Task SaveValuesAsync(
        int productId,
        IReadOnlyList<ProductSpecificationValueInput> values,
        CancellationToken cancellationToken = default);
}

public interface ICatalogDemoSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
