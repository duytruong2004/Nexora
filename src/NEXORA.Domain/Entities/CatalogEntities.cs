namespace NEXORA.Domain.Entities;

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<CategorySpecification> CategorySpecifications { get; set; } = new List<CategorySpecification>();
}

public sealed class Brand
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public sealed class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public int BrandId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }
    public int WarrantyMonths { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Category Category { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductSpecification> Specifications { get; set; } = new List<ProductSpecification>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
    public Inventory? Inventory { get; set; }
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<PromotionProduct> PromotionProducts { get; set; } = new List<PromotionProduct>();
}

public sealed class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class Specification
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string DataType { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<CategorySpecification> Categories { get; set; } = new List<CategorySpecification>();
    public ICollection<ProductSpecification> Products { get; set; } = new List<ProductSpecification>();
    public ICollection<RecommendationRule> RecommendationRules { get; set; } = new List<RecommendationRule>();
}

public sealed class CategorySpecification
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public int SpecificationId { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public Category Category { get; set; } = null!;
    public Specification Specification { get; set; } = null!;
}

public sealed class ProductSpecification
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int SpecificationId { get; set; }
    public string Value { get; set; } = string.Empty;
    public decimal? NumericValue { get; set; }
    public string? DisplayValue { get; set; }
    public Product Product { get; set; } = null!;
    public Specification Specification { get; set; } = null!;
}
