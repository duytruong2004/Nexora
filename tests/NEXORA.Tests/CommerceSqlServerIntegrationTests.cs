using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Commerce;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class CommerceSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task DevelopmentInventorySeeder_SeedsOnlyKnownDemoSkusAndLeavesNewProductsUnstocked()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var connection = new SqlConnectionStringBuilder(connectionString);
        Assert.Equal("NEXORA_IntegrationTests", connection.InitialCatalog, ignoreCase: true);
        Assert.Contains("PC01\\SQLEXPRESS", connection.DataSource, StringComparison.OrdinalIgnoreCase);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var marker = Guid.NewGuid().ToString("N");
        var newProductId = 0;
        try
        {
            await using (var db = new AppDbContext(options))
            {
                var categoryId = await db.Categories.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).FirstAsync();
                var brandId = await db.Brands.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).FirstAsync();
                var activeProductIds = await db.Products.AsNoTracking()
                    .Where(product => product.Sku.StartsWith("NXD-") && product.Status == ProductStatuses.Active &&
                                      product.Category.IsActive && product.Brand.IsActive)
                    .Select(product => product.Id)
                    .ToArrayAsync();
                var before = await db.Inventories.AsNoTracking()
                    .Where(inventory => activeProductIds.Contains(inventory.ProductId))
                    .ToDictionaryAsync(inventory => inventory.ProductId,
                        inventory => new { inventory.QuantityInStock, inventory.ReservedQuantity });
                var newProduct = new Product
                {
                    CategoryId = categoryId,
                    BrandId = brandId,
                    Name = $"NEXORA unstocked seeder QA {marker}",
                    Slug = $"unstocked-seeder-{marker}",
                    Sku = $"QA-{marker}",
                    Price = 1_000m,
                    Status = ProductStatuses.Active,
                    WarrantyMonths = 12,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Products.Add(newProduct);
                await db.SaveChangesAsync();
                newProductId = newProduct.Id;

                var seeder = new DevelopmentInventorySeeder(db, NullLogger<DevelopmentInventorySeeder>.Instance);
                await seeder.SeedMissingAsync();
                var firstPass = await db.Inventories.AsNoTracking()
                    .Where(inventory => activeProductIds.Contains(inventory.ProductId))
                    .ToDictionaryAsync(inventory => inventory.ProductId,
                        inventory => new { inventory.QuantityInStock, inventory.ReservedQuantity });
                await seeder.SeedMissingAsync();
                var secondPass = await db.Inventories.AsNoTracking()
                    .Where(inventory => activeProductIds.Contains(inventory.ProductId))
                    .ToDictionaryAsync(inventory => inventory.ProductId,
                        inventory => new { inventory.QuantityInStock, inventory.ReservedQuantity });

                Assert.All(activeProductIds, productId => Assert.Contains(productId, secondPass.Keys));
                Assert.Equal(firstPass, secondPass);
                foreach (var existing in before)
                {
                    Assert.Equal(existing.Value, firstPass[existing.Key]);
                }
                Assert.All(activeProductIds.Where(productId => !before.ContainsKey(productId)), productId =>
                {
                    Assert.Equal(10, firstPass[productId].QuantityInStock);
                    Assert.Equal(0, firstPass[productId].ReservedQuantity);
                });
                Assert.False(await db.Inventories.AnyAsync(inventory => inventory.ProductId == newProductId));
            }
        }
        finally
        {
            if (newProductId > 0)
            {
                await using var cleanup = new AppDbContext(options);
                var inventory = await cleanup.Inventories.SingleOrDefaultAsync(item => item.ProductId == newProductId);
                if (inventory is not null) cleanup.Inventories.Remove(inventory);
                var product = await cleanup.Products.SingleOrDefaultAsync(item => item.Id == newProductId);
                if (product is not null) cleanup.Products.Remove(product);
                await cleanup.SaveChangesAsync();
            }
        }
    }

    [SqlServerFact]
    public async Task CartAndPromotionPricing_UseSqlDataAndEnforceOwnerAndStockRules()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var connection = new SqlConnectionStringBuilder(connectionString);
        Assert.Equal("NEXORA_IntegrationTests", connection.InitialCatalog, ignoreCase: true);
        Assert.Contains("PC01\\SQLEXPRESS", connection.DataSource, StringComparison.OrdinalIgnoreCase);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new AppDbContext(options);

        var categoryId = await db.Categories.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).FirstAsync();
        var brandId = await db.Brands.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).FirstAsync();
        var staleQaUsers = await db.Users
            .Where(item => item.FullName == "NEXORA Phase 4 Cart QA" &&
                           item.NormalizedUserName != null &&
                           item.NormalizedUserName.StartsWith("PHASE4-") &&
                           item.NormalizedUserName.EndsWith("@EXAMPLE.INVALID"))
            .ToListAsync();
        if (staleQaUsers.Count > 0)
        {
            db.Users.RemoveRange(staleQaUsers);
            await db.SaveChangesAsync();
        }
        var unique = Guid.NewGuid().ToString("N");
        var normalizedEmail = $"PHASE4-{unique}@EXAMPLE.INVALID";
        AppUser? user = null;
        Product? product = null;
        try
        {
            user = new AppUser
            {
                UserName = normalizedEmail,
                NormalizedUserName = normalizedEmail,
                Email = normalizedEmail,
                NormalizedEmail = normalizedEmail,
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                FullName = "NEXORA Phase 4 Cart QA",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            product = new Product
            {
                CategoryId = categoryId,
                BrandId = brandId,
                Name = "NEXORA Phase 4 Cart QA Product",
                Slug = $"phase4-cart-{unique}",
                Sku = $"PH4-{unique}",
                Price = 1_000m,
                Status = ProductStatuses.Active,
                WarrantyMonths = 12,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();

        var inventory = new Inventory { ProductId = product.Id };
        inventory.Receive(10);
        db.Inventories.Add(inventory);
        var percentage = new Promotion
        {
            Name = $"NEXORA Phase 4 QA percent {unique}",
            DiscountType = PromotionDiscountTypes.Percentage,
            DiscountValue = 10m,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1),
            IsActive = true
        };
        var fixedAmount = new Promotion
        {
            Name = $"NEXORA Phase 4 QA fixed {unique}",
            DiscountType = PromotionDiscountTypes.FixedAmount,
            DiscountValue = 450m,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1),
            IsActive = true
        };
        var inactive = new Promotion
        {
            Name = $"NEXORA Phase 4 QA inactive {unique}",
            DiscountType = PromotionDiscountTypes.Percentage,
            DiscountValue = 90m,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1),
            IsActive = false
        };
        db.Promotions.AddRange(percentage, fixedAmount, inactive);
        await db.SaveChangesAsync();
        db.PromotionProducts.AddRange(
            new PromotionProduct { ProductId = product.Id, PromotionId = percentage.Id },
            new PromotionProduct { ProductId = product.Id, PromotionId = fixedAmount.Id },
            new PromotionProduct { ProductId = product.Id, PromotionId = inactive.Id });

        var adminPromotions = new AdminPromotionManagementService(db);
        var managedPromotionId = await adminPromotions.SaveAsync(new PromotionAdminForm(
            null,
            $"NEXORA Phase 4 QA managed {unique}",
            "Integration test promotion",
            PromotionDiscountTypes.Percentage,
            5m,
            DateTime.Now.AddDays(-1),
            DateTime.Now.AddDays(1),
            true,
            [product.Id]));
        Assert.Contains(await adminPromotions.GetPromotionsAsync(), item => item.Id == managedPromotionId && item.IsActive);
        Assert.True(await adminPromotions.SetActiveAsync(managedPromotionId, false));
        Assert.Contains(await adminPromotions.GetPromotionsAsync(), item => item.Id == managedPromotionId && !item.IsActive);

        var cart = new Cart { UserId = user.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

            var pricing = new OrderPricingService(db);
            var service = new CartService(db, pricing);
            Assert.True((await service.AddAsync(user.Id, product.Id, 2)).Succeeded);
            Assert.True((await service.AddAsync(user.Id, product.Id, 2)).Succeeded);

            var cartResult = await service.GetAsync(user.Id);
            var line = Assert.Single(cartResult.Items);
            Assert.Equal(4, line.Quantity);
            Assert.Equal(1_000m, line.UnitPrice);
            Assert.Equal(450m, line.DiscountAmount); // One fixed discount for the product line, not per unit.
            Assert.Equal(3_550m, cartResult.Total);
            Assert.Equal(10, line.AvailableQuantity);
            Assert.True(cartResult.CanCheckout);

            Assert.Equal(CartMutationFailure.InsufficientStock,
                (await service.SetQuantityAsync(user.Id, line.CartItemId, 11)).Failure);
            Assert.Equal(CartMutationFailure.ItemNotFound,
                (await service.RemoveAsync(user.Id + 100_000_000, line.CartItemId)).Failure);
            Assert.True((await service.SetQuantityAsync(user.Id, line.CartItemId, 5)).Succeeded);
            Assert.True((await service.RemoveAsync(user.Id, line.CartItemId)).Succeeded);
            Assert.True((await service.GetAsync(user.Id)).IsEmpty);
        }
        finally
        {
            db.ChangeTracker.Clear();
            var testCart = user is null
                ? null
                : await db.Carts.SingleOrDefaultAsync(item => item.UserId == user.Id);
            if (testCart is not null) db.Carts.Remove(testCart);
            var promotions = await db.Promotions
                .Where(item => item.Name.Contains(unique))
                .ToListAsync();
            db.Promotions.RemoveRange(promotions);
            var testInventory = product is null
                ? null
                : await db.Inventories.SingleOrDefaultAsync(item => item.ProductId == product.Id);
            if (testInventory is not null) db.Inventories.Remove(testInventory);
            var testProduct = product is null
                ? null
                : await db.Products.SingleOrDefaultAsync(item => item.Id == product.Id);
            if (testProduct is not null) db.Products.Remove(testProduct);
            var testUser = await db.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail);
            if (testUser is not null) db.Users.Remove(testUser);
            await db.SaveChangesAsync();
        }
    }
}
