using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NEXORA.Domain.Entities;

namespace NEXORA.Infrastructure.Persistence;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> entity)
    {
        entity.ToTable("categories");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.IsActive).HasDefaultValue(true);
        entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasOne(x => x.Parent).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> entity)
    {
        entity.ToTable("brands");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.LogoUrl).HasMaxLength(2048);
        entity.Property(x => x.IsActive).HasDefaultValue(true);
        entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_brands_name");
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> entity)
    {
        entity.ToTable("products", table =>
        {
            table.HasCheckConstraint("ck_products_price_nonnegative", "[price] >= 0");
            table.HasCheckConstraint("ck_products_discount_price_nonnegative", "[discount_price] IS NULL OR [discount_price] >= 0");
            table.HasCheckConstraint("ck_products_warranty_nonnegative", "[warranty_months] >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(240).IsRequired();
        entity.Property(x => x.Slug).HasMaxLength(260).IsRequired();
        entity.Property(x => x.Sku).HasMaxLength(80).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(4000);
        entity.Property(x => x.Price).HasPrecision(18, 2);
        entity.Property(x => x.DiscountPrice).HasPrecision(18, 2);
        entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
        entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_products_slug");
        entity.HasIndex(x => x.Sku).IsUnique().HasDatabaseName("ux_products_sku");
        entity.HasIndex(x => new { x.CategoryId, x.BrandId, x.Price, x.Status })
            .HasDatabaseName("ix_products_category_brand_price_status");
        entity.HasOne(x => x.Category).WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Brand).WithMany(x => x.Products)
            .HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> entity)
    {
        entity.ToTable("product_images");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.ImageUrl).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => new { x.ProductId, x.SortOrder });
        entity.HasIndex(x => x.ProductId).IsUnique()
            .HasFilter("[is_primary] = 1")
            .HasDatabaseName("ux_product_images_primary_product");
        entity.HasOne(x => x.Product).WithMany(x => x.Images)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SpecificationConfiguration : IEntityTypeConfiguration<Specification>
{
    public void Configure(EntityTypeBuilder<Specification> entity)
    {
        entity.ToTable("specifications");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
        entity.Property(x => x.Unit).HasMaxLength(40);
        entity.Property(x => x.DataType).HasMaxLength(30).IsRequired();
        entity.Property(x => x.IsActive).HasDefaultValue(true);
    }
}

internal sealed class CategorySpecificationConfiguration : IEntityTypeConfiguration<CategorySpecification>
{
    public void Configure(EntityTypeBuilder<CategorySpecification> entity)
    {
        entity.ToTable("category_specifications");
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CategoryId, x.SpecificationId })
            .HasName("ak_category_specifications_category_specification");
        entity.HasOne(x => x.Category).WithMany(x => x.CategorySpecifications)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Specification).WithMany(x => x.Categories)
            .HasForeignKey(x => x.SpecificationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductSpecificationConfiguration : IEntityTypeConfiguration<ProductSpecification>
{
    public void Configure(EntityTypeBuilder<ProductSpecification> entity)
    {
        entity.ToTable("product_specifications");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Value).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.NumericValue).HasPrecision(18, 4);
        entity.Property(x => x.DisplayValue).HasMaxLength(300);
        entity.HasIndex(x => new { x.ProductId, x.SpecificationId })
            .IsUnique().HasDatabaseName("ux_product_specifications_product_specification");
        entity.HasIndex(x => new { x.SpecificationId, x.NumericValue })
            .HasDatabaseName("ix_product_specifications_specification_numeric");
        entity.HasOne(x => x.Product).WithMany(x => x.Specifications)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Specification).WithMany(x => x.Products)
            .HasForeignKey(x => x.SpecificationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> entity)
    {
        entity.ToTable("carts");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("ux_carts_user_id");
        entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasOne<NEXORA.Infrastructure.Identity.AppUser>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> entity)
    {
        entity.ToTable("cart_items", table => table.HasCheckConstraint("ck_cart_items_quantity_positive", "[quantity] > 0"));
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.CartId, x.ProductId }).IsUnique()
            .HasDatabaseName("ux_cart_items_cart_product");
        entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasOne(x => x.Cart).WithMany(x => x.Items)
            .HasForeignKey(x => x.CartId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Product).WithMany(x => x.CartItems)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> entity)
    {
        entity.ToTable("orders", table =>
        {
            table.HasCheckConstraint("ck_orders_total_amount_nonnegative", "[total_amount] >= 0");
            table.HasCheckConstraint("ck_orders_status_valid", "[status] IN ('PENDING','CONFIRMED','PREPARING','EXPORTED','SHIPPING','COMPLETED','CANCELLED')");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.Id, x.UserId })
            .HasName("ak_orders_id_user_id");
        entity.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Phone).HasMaxLength(32).IsRequired();
        entity.Property(x => x.ShippingAddress).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
        entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
        entity.Property(x => x.Note).HasMaxLength(2000);
        entity.Property(x => x.CancelReason).HasMaxLength(1000);
        entity.Property(x => x.OrderDate).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => new { x.UserId, x.Status, x.OrderDate })
            .HasDatabaseName("ix_orders_user_status_order_date");
        entity.HasIndex(x => new { x.Status, x.OrderDate })
            .HasDatabaseName("ix_orders_status_order_date");
        entity.HasIndex(x => x.CheckoutRequestId)
            .IsUnique()
            .HasFilter("[checkout_request_id] IS NOT NULL")
            .HasDatabaseName("ux_orders_checkout_request_id");
        entity.HasOne<NEXORA.Infrastructure.Identity.AppUser>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<NEXORA.Infrastructure.Identity.AppUser>().WithMany()
            .HasForeignKey(x => x.ConfirmedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderDetailConfiguration : IEntityTypeConfiguration<OrderDetail>
{
    public void Configure(EntityTypeBuilder<OrderDetail> entity)
    {
        entity.ToTable("order_details", table =>
        {
            table.HasCheckConstraint("ck_order_details_quantity_positive", "[quantity] > 0");
            table.HasCheckConstraint("ck_order_details_unit_price_nonnegative", "[unit_price] >= 0");
            table.HasCheckConstraint("ck_order_details_discount_nonnegative", "[discount_amount] >= 0");
            table.HasCheckConstraint("ck_order_details_total_nonnegative", "[total_price] >= 0");
            table.HasCheckConstraint("ck_order_details_total_matches_formula",
                "[total_price] = ([unit_price] * [quantity]) - [discount_amount]");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.Id, x.ProductId, x.OrderId })
            .HasName("ak_order_details_id_product_order");
        entity.Property(x => x.ProductName).HasMaxLength(240).IsRequired();
        entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
        entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        entity.Property(x => x.TotalPrice).HasPrecision(18, 2);
        entity.HasOne(x => x.Order).WithMany(x => x.Details)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Product).WithMany(x => x.OrderDetails)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> entity)
    {
        entity.ToTable("inventories", table =>
            table.HasCheckConstraint("ck_inventories_quantities_valid", "[quantity_in_stock] >= 0 AND [reserved_quantity] >= 0 AND [reserved_quantity] <= [quantity_in_stock]"));
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.ProductId).IsUnique().HasDatabaseName("ux_inventories_product_id");
        entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        entity.Ignore(x => x.AvailableQuantity);
        entity.HasOne(x => x.Product).WithOne(x => x.Inventory)
            .HasForeignKey<Inventory>(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> entity)
    {
        entity.ToTable("inventory_transactions", table =>
        {
            table.HasCheckConstraint("ck_inventory_transactions_quantity_positive", "[quantity] > 0");
            table.HasCheckConstraint("ck_inventory_transactions_type_valid", "[transaction_type] IN ('IN','OUT','ADJUST','RESERVE','RELEASE')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.TransactionType).HasMaxLength(20).IsRequired();
        entity.Property(x => x.Note).HasMaxLength(1000);
        entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => new { x.ProductId, x.CreatedAt });
        entity.HasOne(x => x.Product).WithMany(x => x.InventoryTransactions)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<NEXORA.Infrastructure.Identity.AppUser>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Order).WithMany(x => x.InventoryTransactions)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> entity)
    {
        entity.ToTable("favorites");
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.UserId, x.ProductId }).IsUnique()
            .HasDatabaseName("ux_favorites_user_product");
        entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasOne<NEXORA.Infrastructure.Identity.AppUser>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Product).WithMany(x => x.Favorites)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> entity)
    {
        entity.ToTable("reviews", table => table.HasCheckConstraint("ck_reviews_rating_range", "[rating] >= 1 AND [rating] <= 5"));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Comment).HasMaxLength(3000);
        entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
        entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => x.OrderDetailId).IsUnique()
            .HasDatabaseName("ux_reviews_order_detail");
        entity.HasOne<NEXORA.Infrastructure.Identity.AppUser>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Product).WithMany(x => x.Reviews)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.OrderDetail).WithMany(x => x.Reviews)
            .HasForeignKey(x => new { x.OrderDetailId, x.ProductId, x.OrderId })
            .HasPrincipalKey(x => new { x.Id, x.ProductId, x.OrderId })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<NEXORA.Domain.Entities.Order>().WithMany(x => x.Reviews)
            .HasForeignKey(x => new { x.OrderId, x.UserId })
            .HasPrincipalKey(x => new { x.Id, x.UserId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> entity)
    {
        entity.ToTable("promotions", table =>
        {
            table.HasCheckConstraint("ck_promotions_dates_valid", "[end_date] >= [start_date]");
            table.HasCheckConstraint("ck_promotions_discount_nonnegative", "[discount_value] >= 0");
            table.HasCheckConstraint("ck_promotions_discount_type_valid",
                "[discount_type] IN ('PERCENTAGE','FIXED_AMOUNT')");
            table.HasCheckConstraint("ck_promotions_percentage_range",
                "[discount_type] <> 'PERCENTAGE' OR [discount_value] <= 100");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(2000);
        entity.Property(x => x.DiscountType).HasMaxLength(30).IsRequired();
        entity.Property(x => x.DiscountValue).HasPrecision(18, 2);
        entity.HasIndex(x => new { x.IsActive, x.StartDate, x.EndDate });
    }
}

internal sealed class PromotionProductConfiguration : IEntityTypeConfiguration<PromotionProduct>
{
    public void Configure(EntityTypeBuilder<PromotionProduct> entity)
    {
        entity.ToTable("promotion_products");
        entity.HasKey(x => new { x.PromotionId, x.ProductId });
        entity.HasOne(x => x.Promotion).WithMany(x => x.Products)
            .HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Product).WithMany(x => x.PromotionProducts)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UsageNeedConfiguration : IEntityTypeConfiguration<UsageNeed>
{
    public void Configure(EntityTypeBuilder<UsageNeed> entity)
    {
        entity.ToTable("usage_needs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(80).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.IsActive).HasDefaultValue(true);
        entity.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_usage_needs_code");
        entity.HasIndex(x => new { x.Code, x.IsActive }).HasDatabaseName("ix_usage_needs_code_active");
    }
}

internal sealed class RecommendationRuleConfiguration : IEntityTypeConfiguration<RecommendationRule>
{
    public void Configure(EntityTypeBuilder<RecommendationRule> entity)
    {
        entity.ToTable("recommendation_rules", table =>
        {
            table.HasCheckConstraint("ck_recommendation_rules_weight_nonnegative", "[weight] >= 0");
            table.HasCheckConstraint("ck_recommendation_rules_operator_valid",
                "[operator] IN ('EQ','NE','GT','GTE','LT','LTE','CONTAINS')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Operator).HasMaxLength(30).IsRequired();
        entity.Property(x => x.ThresholdValue).HasMaxLength(300).IsRequired();
        entity.Property(x => x.Weight).HasPrecision(9, 4);
        entity.Property(x => x.IsActive).HasDefaultValue(true);
        entity.HasIndex(x => new { x.UsageNeedId, x.CategoryId, x.IsActive })
            .HasDatabaseName("ix_recommendation_rules_need_category_active");
        entity.HasOne(x => x.UsageNeed).WithMany(x => x.Rules)
            .HasForeignKey(x => x.UsageNeedId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<CategorySpecification>().WithMany()
            .HasForeignKey(x => new { x.CategoryId, x.SpecificationId })
            .HasPrincipalKey(x => new { x.CategoryId, x.SpecificationId })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Specification).WithMany(x => x.RecommendationRules)
            .HasForeignKey(x => x.SpecificationId).OnDelete(DeleteBehavior.Restrict);
    }
}
