using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.AspNetCore.Identity;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;

namespace NEXORA.Tests;

public sealed class AppDbContextModelTests
{
    private static readonly string[] BusinessTables =
    [
        "roles", "users", "user_roles",
        "categories", "brands", "products", "product_images",
        "specifications", "category_specifications", "product_specifications",
        "carts", "cart_items", "orders", "order_details",
        "inventories", "inventory_transactions",
        "favorites", "reviews", "promotions", "promotion_products",
        "usage_needs", "recommendation_rules"
    ];

    [Fact]
    public void Model_MapsAllTwentyTwoBusinessTablesAndIdentitySupportTables()
    {
        using var context = CreateContext();
        var tables = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(BusinessTables, table => Assert.Contains(table, tables));
        Assert.Contains("AspNetUserClaims", tables);
        Assert.Contains("AspNetUserLogins", tables);
        Assert.Contains("AspNetUserTokens", tables);
        Assert.Contains("AspNetRoleClaims", tables);
        Assert.Equal(26, tables.Count);
    }

    [Fact]
    public void Identity_MapsToBusinessTablesAndDefinesTheFourRequiredRoles()
    {
        using var context = CreateContext();
        var user = context.Model.FindEntityType(typeof(AppUser))!;
        var role = context.Model.FindEntityType(typeof(AppRole))!;
        var userRole = context.Model.FindEntityType(typeof(AppUserRole))!;

        Assert.Equal("users", user.GetTableName());
        Assert.Equal("password_hash", user.FindProperty(nameof(AppUser.PasswordHash))!.GetColumnName());
        Assert.Equal("roles", role.GetTableName());
        Assert.Equal("user_roles", userRole.GetTableName());
        Assert.Equal([RoleNames.Customer, RoleNames.Sales, RoleNames.Warehouse, RoleNames.Admin], RoleNames.All);
    }

    [Fact]
    public void Model_ConfiguresForeignKeysUniqueConstraintsAndChecks()
    {
        using var context = CreateContext();

        var cart = context.Model.FindEntityType("NEXORA.Domain.Entities.CartItem")!;
        Assert.Contains(cart.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["CartId", "ProductId"]));
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var relationalModel = designModel.GetRelationalModel();
        Assert.Contains(relationalModel.Tables.Single(table => table.Name == "cart_items").CheckConstraints,
            constraint => constraint.Name == "ck_cart_items_quantity_positive");

        var favorite = context.Model.FindEntityType("NEXORA.Domain.Entities.Favorite")!;
        Assert.Contains(favorite.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["UserId", "ProductId"]));

        var order = context.Model.FindEntityType("NEXORA.Domain.Entities.Order")!;
        Assert.Contains(order.GetForeignKeys(), foreignKey => foreignKey.Properties.Any(property => property.Name == "UserId"));
        Assert.Contains(relationalModel.Tables.Single(table => table.Name == "orders").CheckConstraints,
            constraint => constraint.Name == "ck_orders_status_valid");
    }

    [Fact]
    public void InventoryRowVersion_IsConfiguredAsSqlServerConcurrencyToken()
    {
        using var context = CreateContext();
        var inventory = context.Model.FindEntityType("NEXORA.Domain.Entities.Inventory")!;
        var rowVersion = inventory.FindProperty("RowVersion")!;

        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
        Assert.Equal("row_version", rowVersion.GetColumnName());
    }

    [Fact]
    public void Model_ConstrainsRecommendationOperatorsWeightsAndCategoryMembership()
    {
        using var context = CreateContext();
        var rule = context.Model.FindEntityType(typeof(RecommendationRule))!;
        var categorySpecification = context.Model.FindEntityType(typeof(CategorySpecification))!;
        var relationalModel = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var ruleTable = relationalModel.Tables.Single(table => table.Name == "recommendation_rules");
        var categorySpecTable = relationalModel.Tables.Single(table => table.Name == "category_specifications");

        Assert.Equal(7, RecommendationRuleOperators.All.Count);
        Assert.All(["EQ", "NE", "GT", "GTE", "LT", "LTE", "CONTAINS"],
            operation => Assert.Contains(operation, RecommendationRuleOperators.All));
        Assert.Contains(ruleTable.CheckConstraints, check =>
            check.Name == "ck_recommendation_rules_operator_valid" &&
            check.Sql.Contains("'CONTAINS'", StringComparison.Ordinal));
        Assert.Contains(ruleTable.CheckConstraints, check =>
            check.Name == "ck_recommendation_rules_weight_nonnegative" &&
            check.Sql.Contains(">= 0", StringComparison.Ordinal));
        Assert.Contains(rule.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(CategorySpecification) &&
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["CategoryId", "SpecificationId"]));
        Assert.Contains(categorySpecTable.UniqueConstraints, key =>
            key.Name == "ak_category_specifications_category_specification");
    }

    [Fact]
    public void Model_EnforcesReviewOwnershipAndMatchingOrderDetailProductWithCompositeForeignKeys()
    {
        using var context = CreateContext();
        var review = context.Model.FindEntityType(typeof(Review))!;
        var orderForeignKey = Assert.Single(review.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Order));
        var detailForeignKey = Assert.Single(review.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(OrderDetail));

        Assert.Equal(["OrderId", "UserId"], orderForeignKey.Properties.Select(property => property.Name));
        Assert.Equal(["Id", "UserId"], orderForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(["OrderDetailId", "ProductId", "OrderId"],
            detailForeignKey.Properties.Select(property => property.Name));
        Assert.Equal(["Id", "ProductId", "OrderId"],
            detailForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Contains(review.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["OrderDetailId"]));
    }

    [Fact]
    public void Model_ConstrainsOrderDetailFormulaImagesPromotionsAndCompletedAt()
    {
        using var context = CreateContext();
        var relationalModel = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var details = relationalModel.Tables.Single(table => table.Name == "order_details");
        var promotions = relationalModel.Tables.Single(table => table.Name == "promotions");
        var orders = context.Model.FindEntityType(typeof(Order))!;
        var images = context.Model.FindEntityType(typeof(ProductImage))!;

        Assert.Contains(details.CheckConstraints, check =>
            check.Name == "ck_order_details_total_matches_formula" &&
            check.Sql.Contains("unit_price", StringComparison.Ordinal) &&
            check.Sql.Contains("quantity", StringComparison.Ordinal) &&
            check.Sql.Contains("discount_amount", StringComparison.Ordinal));
        Assert.NotNull(orders.FindProperty(nameof(Order.CompletedAt)));
        Assert.Contains(images.GetIndexes(), index => index.IsUnique &&
            index.GetFilter() == "[is_primary] = 1" &&
            index.Properties.Select(property => property.Name).SequenceEqual(["ProductId"]));
        Assert.Contains(promotions.CheckConstraints, check => check.Name == "ck_promotions_discount_type_valid");
        Assert.Contains(promotions.CheckConstraints, check => check.Name == "ck_promotions_percentage_range");
    }

    [Fact]
    public void Model_KeepsDynamicSpecificationsUniqueAndLinkedToCategoriesAndProducts()
    {
        using var context = CreateContext();
        var assignments = context.Model.FindEntityType(typeof(CategorySpecification))!;
        var values = context.Model.FindEntityType(typeof(ProductSpecification))!;

        Assert.Contains(assignments.GetKeys(), key =>
            key.Properties.Select(property => property.Name).SequenceEqual(["CategoryId", "SpecificationId"]));
        Assert.Contains(values.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["ProductId", "SpecificationId"]));
        Assert.Contains(values.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(["SpecificationId", "NumericValue"]));
        Assert.Contains(values.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Product));
        Assert.Contains(values.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Specification));
    }

    [Fact]
    public void Identity_UsesIntegerKeysStandardUniqueIndexesAndExpectedLengths()
    {
        using var context = CreateContext();
        var user = context.Model.FindEntityType(typeof(AppUser))!;
        var role = context.Model.FindEntityType(typeof(AppRole))!;
        var userRole = context.Model.FindEntityType(typeof(AppUserRole))!;
        var login = context.Model.FindEntityType(typeof(IdentityUserLogin<int>))!;
        var token = context.Model.FindEntityType(typeof(IdentityUserToken<int>))!;

        Assert.Equal(typeof(int), user.FindProperty(nameof(AppUser.Id))!.ClrType);
        Assert.Equal(typeof(int), role.FindProperty(nameof(AppRole.Id))!.ClrType);
        Assert.Equal(typeof(int), userRole.FindProperty(nameof(AppUserRole.UserId))!.ClrType);
        Assert.Equal(typeof(int), userRole.FindProperty(nameof(AppUserRole.RoleId))!.ClrType);
        Assert.Equal(256, user.FindProperty(nameof(AppUser.NormalizedUserName))!.GetMaxLength());
        Assert.Equal(256, user.FindProperty(nameof(AppUser.NormalizedEmail))!.GetMaxLength());
        Assert.Contains(user.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(AppUser.NormalizedUserName)]));
        Assert.Contains(user.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(AppUser.NormalizedEmail)]));
        Assert.Contains(role.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(AppRole.NormalizedName)]));
        Assert.Equal(128, login.FindProperty(nameof(IdentityUserLogin<int>.LoginProvider))!.GetMaxLength());
        Assert.Equal(128, login.FindProperty(nameof(IdentityUserLogin<int>.ProviderKey))!.GetMaxLength());
        Assert.Equal(128, token.FindProperty(nameof(IdentityUserToken<int>.LoginProvider))!.GetMaxLength());
        Assert.Equal(128, token.FindProperty(nameof(IdentityUserToken<int>.Name))!.GetMaxLength());
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=NEXORA_ModelOnly;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        return new AppDbContext(options);
    }
}
