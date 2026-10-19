using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NEXORA.Application.Catalog;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Catalog;
using NEXORA.Infrastructure.Commerce;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ProductInventorySqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task LaptopProductWorkflow_CreatesDraft_StoresSpecifications_AndActivatesWithZeroStock()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var marker = Guid.NewGuid().ToString("N");
        var sku = $"NEXORA-LAPTOP-QA-{marker}";
        var productId = 0;

        try
        {
            await using var db = new AppDbContext(options);
            var categoryId = await db.Categories.AsNoTracking()
                .Where(item => item.Name == "Laptop" && item.IsActive)
                .Select(item => item.Id)
                .SingleAsync();
            var brandId = await db.Brands.AsNoTracking()
                .Where(item => item.Name == "NEXORA Demo" && item.IsActive)
                .Select(item => item.Id)
                .SingleAsync();

            var products = new ProductManagementService(db);
            productId = await products.SaveAsync(new ProductInput(
                null, categoryId, brandId, $"NEXORA QA Laptop {marker}", sku,
                "Temporary integration-test product; removed after this test.",
                24_990_000m, null, 12, ProductStatuses.Inactive));

            var inventory = await db.Inventories.AsNoTracking().SingleAsync(item => item.ProductId == productId);
            Assert.Equal(0, inventory.QuantityInStock);
            Assert.Equal(0, inventory.ReservedQuantity);

            var specificationService = new ProductSpecificationManagementService(db);
            var editor = Assert.IsType<ProductSpecificationEditor>(await specificationService.GetEditorAsync(productId));
            Assert.Equal("Laptop", editor.CategoryName);
            Assert.Equal(6, editor.Values.Count);

            static (string Value, string? Display) ExampleValue(ProductSpecificationEditorValue spec) => spec.Name switch
            {
                "CPU" => ("Demo CPU QA", "Demo CPU QA"),
                "RAM" => ("16", "16 GB"),
                "SSD" => ("512", "512 GB"),
                "GPU" => ("Demo GPU QA", "Demo GPU QA"),
                "Trọng lượng" => ("1.45", "1.45 kg"),
                "Kích thước màn hình" => ("14", "14 inch"),
                _ => spec.DataType switch
                {
                    SpecificationDataTypes.Number => ("1", spec.Unit is null ? "1" : $"1 {spec.Unit}"),
                    SpecificationDataTypes.Boolean => ("true", "Có"),
                    _ => ("QA demo", "QA demo")
                }
            };

            await specificationService.SaveValuesAsync(productId, editor.Values
                .Select(spec =>
                {
                    var example = ExampleValue(spec);
                    return new ProductSpecificationValueInput(spec.SpecificationId, example.Value, example.Display);
                })
                .ToArray());

            await products.SaveAsync(new ProductInput(
                productId, categoryId, brandId, $"NEXORA QA Laptop {marker}", sku,
                "Temporary integration-test product; removed after this test.",
                24_990_000m, null, 12, ProductStatuses.Active));

            var saved = Assert.IsType<ProductAdminDetails>(await products.GetAsync(productId));
            Assert.Equal(ProductStatuses.Active, saved.Status);
            Assert.Equal(6, saved.Specifications.Count);
            Assert.Contains(saved.Specifications, item => item.Name == "RAM" && item.DisplayValue == "16 GB");
            Assert.Contains(saved.Specifications, item => item.Name == "SSD" && item.DisplayValue == "512 GB");

            var adminRow = Assert.Single((await products.SearchAsync(new ProductAdminFilter(sku, null, null, null))).Items);
            Assert.Equal(0, adminRow.QuantityInStock);
            Assert.Equal(0, adminRow.ReservedQuantity);
            Assert.Equal(0, adminRow.AvailableQuantity);
        }
        finally
        {
            await using var cleanup = new AppDbContext(options);
            var product = productId > 0
                ? await cleanup.Products.SingleOrDefaultAsync(item => item.Id == productId)
                : await cleanup.Products.SingleOrDefaultAsync(item => item.Sku == sku);
            if (product is not null)
            {
                var specifications = await cleanup.ProductSpecifications
                    .Where(item => item.ProductId == product.Id).ToListAsync();
                cleanup.ProductSpecifications.RemoveRange(specifications);
                var inventory = await cleanup.Inventories.SingleOrDefaultAsync(item => item.ProductId == product.Id);
                if (inventory is not null) cleanup.Inventories.Remove(inventory);
                cleanup.Products.Remove(product);
                await cleanup.SaveChangesAsync();
            }
        }
    }

    [SqlServerFact]
    public async Task ProductCreation_CreatesZeroInventory_AndCannotBeAddedOrCheckedOutWithoutStock()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var marker = Guid.NewGuid().ToString("N");
        var sku = $"NEXORA-INV-{marker}";
        var email = $"nexora-inventory-{marker}@example.invalid";
        var categoryId = 0;
        var brandId = 0;
        var productId = 0;
        var userId = 0;

        try
        {
            await using (var setup = new AppDbContext(options))
            {
                var category = new Category { Name = $"NEXORA inventory QA {marker}", IsActive = true };
                var brand = new Brand { Name = $"NEXORA inventory QA {marker}", IsActive = true };
                setup.Categories.Add(category);
                setup.Brands.Add(brand);
                await setup.SaveChangesAsync();
                categoryId = category.Id;
                brandId = brand.Id;
            }

            await using (var catalogDb = new AppDbContext(options))
            {
                var service = new ProductManagementService(catalogDb);
                productId = await service.SaveAsync(new ProductInput(
                    null, categoryId, brandId, $"NEXORA inventory QA {marker}", sku,
                    null, 1_000m, null, 12, ProductStatuses.Active));

                await new DevelopmentInventorySeeder(
                    catalogDb,
                    NullLogger<DevelopmentInventorySeeder>.Instance).SeedMissingAsync();
                var inventory = await catalogDb.Inventories.AsNoTracking()
                    .SingleAsync(item => item.ProductId == productId);
                Assert.Equal(0, inventory.QuantityInStock);
                Assert.Equal(0, inventory.ReservedQuantity);

                var row = Assert.Single((await service.SearchAsync(new ProductAdminFilter(sku, null, null, null))).Items);
                Assert.Equal(0, row.QuantityInStock);
                Assert.Equal(0, row.ReservedQuantity);
                Assert.Equal(0, row.AvailableQuantity);
            }

            await using (var commerceDb = new AppDbContext(options))
            {
                var user = new AppUser
                {
                    UserName = email,
                    NormalizedUserName = email.ToUpperInvariant(),
                    Email = email,
                    NormalizedEmail = email.ToUpperInvariant(),
                    EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    FullName = "NEXORA inventory QA",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                commerceDb.Users.Add(user);
                await commerceDb.SaveChangesAsync();
                userId = user.Id;

                var cart = new Cart { UserId = user.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                cart.Items.Add(new CartItem
                {
                    ProductId = productId,
                    Quantity = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                commerceDb.Carts.Add(cart);
                await commerceDb.SaveChangesAsync();

                var pricing = new OrderPricingService(commerceDb);
                var quote = await pricing.GetCurrentQuoteAsync(user.Id);
                Assert.False(quote.CanCheckout);
                Assert.Equal(0, Assert.Single(quote.Lines).AvailableQuantity);

                var cartService = new CartService(commerceDb, pricing);
                Assert.Equal(CartMutationFailure.OutOfStock,
                    (await cartService.AddAsync(user.Id, productId, 1)).Failure);

                var checkout = new CheckoutService(commerceDb, pricing, NullLogger<CheckoutService>.Instance);
                var result = await checkout.PlaceOrderAsync(user.Id, new PlaceOrderInput(
                    Guid.NewGuid(), "NEXORA Inventory QA", "+84901234567", "12 Nguyen Hue, District 1", null,
                    quote.Fingerprint));
                Assert.Equal(PlaceOrderFailure.InsufficientStock, result.Failure);
                Assert.Equal(0, await commerceDb.Orders.CountAsync(order => order.UserId == user.Id));
            }
        }
        finally
        {
            await CleanupAsync(options, productId, userId, categoryId, brandId);
        }
    }

    [SqlServerFact]
    public async Task CatalogProjectsAvailableStock_ForCardsDetailsAndComparison_AndCartUsesCurrentStock()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        var marker = Guid.NewGuid().ToString("N");
        var categoryId = 0;
        var brandId = 0;
        var userId = 0;
        var productIds = new List<int>();

        try
        {
            await using (var setup = new AppDbContext(options))
            {
                var category = new Category { Name = $"NEXORA stock projection QA {marker}", IsActive = true };
                var brand = new Brand { Name = $"NEXORA stock projection QA {marker}", IsActive = true };
                setup.Categories.Add(category);
                setup.Brands.Add(brand);
                await setup.SaveChangesAsync();
                categoryId = category.Id;
                brandId = brand.Id;

                var user = new AppUser
                {
                    UserName = $"NEXORA-STOCK-{marker}@EXAMPLE.INVALID",
                    NormalizedUserName = $"NEXORA-STOCK-{marker}@EXAMPLE.INVALID",
                    Email = $"nexora-stock-{marker}@example.invalid",
                    NormalizedEmail = $"NEXORA-STOCK-{marker}@EXAMPLE.INVALID",
                    EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    FullName = "NEXORA catalog stock QA",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                setup.Users.Add(user);
                await setup.SaveChangesAsync();
                userId = user.Id;
            }

            var skuPrefix = $"NX-STOCK-{marker}";
            var skus = new[] { $"{skuPrefix}-ZERO", $"{skuPrefix}-RESERVED", $"{skuPrefix}-AVAILABLE", $"{skuPrefix}-MISSING" };
            await using (var setup = new AppDbContext(options))
            {
                var products = new ProductManagementService(setup);
                foreach (var sku in skus)
                {
                    productIds.Add(await products.SaveAsync(new ProductInput(
                        null, categoryId, brandId, $"NEXORA stock projection {sku}", sku,
                        "Temporary SQL integration test product.", 10_000m, null, 12, ProductStatuses.Active)));
                }

                var missingInventory = await setup.Inventories.SingleAsync(item => item.ProductId == productIds[3]);
                var reservedInventory = await setup.Inventories.SingleAsync(item => item.ProductId == productIds[1]);
                var availableInventory = await setup.Inventories.SingleAsync(item => item.ProductId == productIds[2]);
                reservedInventory.Receive(5);
                reservedInventory.Reserve(5);
                availableInventory.Receive(7);
                availableInventory.Reserve(2);
                setup.Inventories.Remove(missingInventory);
                await setup.SaveChangesAsync();
            }

            await using (var queryDb = new AppDbContext(options))
            {
                var catalog = new CustomerCatalogQueryService(queryDb);
                var page = await catalog.SearchAsync(new CatalogSearchFilter(marker, categoryId, null, null, null, "name-asc"));
                Assert.Equal(4, page.Items.Count);
                var cardBySku = page.Items.ToDictionary(item => item.Sku);
                Assert.Equal(0, cardBySku[skus[0]].AvailableQuantity); // Inventory initialized to 0/0.
                Assert.Equal(0, cardBySku[skus[1]].AvailableQuantity); // Fully reserved stock.
                Assert.Equal(5, cardBySku[skus[2]].AvailableQuantity); // 7 in stock - 2 reserved.
                Assert.Null(cardBySku[skus[3]].AvailableQuantity); // Missing inventory record.

                var details = await catalog.GetProductAsync(productIds[2]);
                Assert.Equal(5, details?.AvailableQuantity);
                var comparison = await catalog.CompareAsync([productIds[2], productIds[3]]);
                Assert.Equal(new int?[] { 5, null }, comparison.Products.Select(item => item.AvailableQuantity).ToArray());

                var home = await catalog.GetHomeAsync();
                Assert.NotEmpty(home.Products);
                var homeTestProducts = home.Products.Where(item => skus.Contains(item.Sku)).ToDictionary(item => item.Sku);
                Assert.Equal(4, homeTestProducts.Count);
                Assert.Equal(0, homeTestProducts[skus[0]].AvailableQuantity);
                Assert.Equal(0, homeTestProducts[skus[1]].AvailableQuantity);
                Assert.Equal(5, homeTestProducts[skus[2]].AvailableQuantity);
                Assert.Null(homeTestProducts[skus[3]].AvailableQuantity);

                var pricing = new OrderPricingService(queryDb);
                var cart = new CartService(queryDb, pricing);
                Assert.Equal(CartMutationFailure.OutOfStock, (await cart.AddAsync(userId, productIds[1], 1)).Failure);
                Assert.Equal(CartMutationFailure.OutOfStock, (await cart.AddAsync(userId, productIds[0], 1)).Failure);
                Assert.Equal(CartMutationFailure.OutOfStock, (await cart.AddAsync(userId, productIds[3], 1)).Failure);
                Assert.True((await cart.AddAsync(userId, productIds[2], 2)).Succeeded);
                Assert.Equal(CartMutationFailure.InsufficientStock,
                    (await cart.AddAsync(userId, productIds[2], 4)).Failure);
            }
        }
        finally
        {
            await using var cleanup = new AppDbContext(options);
            var products = productIds.Count == 0
                ? await cleanup.Products.Where(item => item.Sku.Contains(marker)).ToListAsync()
                : await cleanup.Products.Where(item => productIds.Contains(item.Id)).ToListAsync();
            var cleanupIds = products.Select(item => item.Id).ToArray();
            var carts = await cleanup.Carts.Where(item => item.UserId == userId).ToListAsync();
            var cartIds = carts.Select(item => item.Id).ToArray();
            var cartItems = await cleanup.CartItems.Where(item => cartIds.Contains(item.CartId)).ToListAsync();
            cleanup.CartItems.RemoveRange(cartItems);
            cleanup.Carts.RemoveRange(carts);
            var inventoryTransactions = await cleanup.InventoryTransactions
                .Where(item => cleanupIds.Contains(item.ProductId)).ToListAsync();
            cleanup.InventoryTransactions.RemoveRange(inventoryTransactions);
            var productSpecifications = await cleanup.ProductSpecifications
                .Where(item => cleanupIds.Contains(item.ProductId)).ToListAsync();
            cleanup.ProductSpecifications.RemoveRange(productSpecifications);
            var inventories = await cleanup.Inventories.Where(item => cleanupIds.Contains(item.ProductId)).ToListAsync();
            cleanup.Inventories.RemoveRange(inventories);
            cleanup.Products.RemoveRange(products);
            if (userId > 0)
            {
                var user = await cleanup.Users.SingleOrDefaultAsync(item => item.Id == userId);
                if (user is not null) cleanup.Users.Remove(user);
            }
            if (brandId > 0)
            {
                var brand = await cleanup.Brands.SingleOrDefaultAsync(item => item.Id == brandId);
                if (brand is not null) cleanup.Brands.Remove(brand);
            }
            if (categoryId > 0)
            {
                var category = await cleanup.Categories.SingleOrDefaultAsync(item => item.Id == categoryId);
                if (category is not null) cleanup.Categories.Remove(category);
            }
            await cleanup.SaveChangesAsync();
        }
    }

    [SqlServerFact]
    public async Task ProductCreation_RollsBackProductWhenInventoryInsertFails()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var marker = Guid.NewGuid().ToString("N");
        var sku = $"NEXORA-ROLLBACK-{marker}";
        var categoryId = 0;
        var brandId = 0;
        try
        {
            var plainOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
            await using (var setup = new AppDbContext(plainOptions))
            {
                var category = new Category { Name = $"NEXORA rollback QA {marker}", IsActive = true };
                var brand = new Brand { Name = $"NEXORA rollback QA {marker}", IsActive = true };
                setup.Categories.Add(category);
                setup.Brands.Add(brand);
                await setup.SaveChangesAsync();
                categoryId = category.Id;
                brandId = brand.Id;
            }

            var failingOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString)
                .AddInterceptors(new RejectInventoryInsertInterceptor())
                .Options;
            await using (var failingDb = new AppDbContext(failingOptions))
            {
                var service = new ProductManagementService(failingDb);
                var exception = await Assert.ThrowsAsync<DbUpdateException>(() => service.SaveAsync(new ProductInput(
                    null, categoryId, brandId, $"NEXORA rollback QA {marker}", sku,
                    null, 1_000m, null, 12, ProductStatuses.Active)));
                Assert.IsType<InventoryInsertRejectedException>(exception.InnerException);
            }

            await using var verify = new AppDbContext(plainOptions);
            Assert.False(await verify.Products.AnyAsync(product => product.Sku == sku));
            Assert.False(await verify.Inventories.AnyAsync(inventory => inventory.Product.Sku == sku));
        }
        finally
        {
            var cleanupOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
            await using var cleanup = new AppDbContext(cleanupOptions);
            var partialProducts = await cleanup.Products.Where(product => product.Sku == sku).ToListAsync();
            if (partialProducts.Count > 0)
            {
                var partialIds = partialProducts.Select(product => product.Id).ToArray();
                var inventories = await cleanup.Inventories.Where(item => partialIds.Contains(item.ProductId)).ToListAsync();
                cleanup.Inventories.RemoveRange(inventories);
                cleanup.Products.RemoveRange(partialProducts);
            }
            if (brandId > 0)
            {
                var brand = await cleanup.Brands.SingleOrDefaultAsync(item => item.Id == brandId);
                if (brand is not null) cleanup.Brands.Remove(brand);
            }
            if (categoryId > 0)
            {
                var category = await cleanup.Categories.SingleOrDefaultAsync(item => item.Id == categoryId);
                if (category is not null) cleanup.Categories.Remove(category);
            }
            await cleanup.SaveChangesAsync();
        }
    }

    private static async Task CleanupAsync(
        DbContextOptions<AppDbContext> options,
        int productId,
        int userId,
        int categoryId,
        int brandId)
    {
        await using var cleanup = new AppDbContext(options);
        var userOrders = userId > 0
            ? await cleanup.Orders.Where(order => order.UserId == userId).ToListAsync()
            : [];
        var orderIds = userOrders.Select(order => order.Id).ToArray();
        var inventoryTransactions = await cleanup.InventoryTransactions
            .Where(item => (userId > 0 && item.UserId == userId) || orderIds.Contains(item.OrderId ?? 0) ||
                           (productId > 0 && item.ProductId == productId))
            .ToListAsync();
        cleanup.InventoryTransactions.RemoveRange(inventoryTransactions);
        var orderDetails = await cleanup.OrderDetails.Where(item => orderIds.Contains(item.OrderId)).ToListAsync();
        cleanup.OrderDetails.RemoveRange(orderDetails);
        cleanup.Orders.RemoveRange(userOrders);
        if (userId > 0)
        {
            var cartItems = await cleanup.CartItems.Where(item => item.Cart.UserId == userId).ToListAsync();
            cleanup.CartItems.RemoveRange(cartItems);
            var carts = await cleanup.Carts.Where(item => item.UserId == userId).ToListAsync();
            cleanup.Carts.RemoveRange(carts);
            var user = await cleanup.Users.SingleOrDefaultAsync(item => item.Id == userId);
            if (user is not null) cleanup.Users.Remove(user);
        }
        if (productId > 0)
        {
            var inventory = await cleanup.Inventories.SingleOrDefaultAsync(item => item.ProductId == productId);
            if (inventory is not null) cleanup.Inventories.Remove(inventory);
            var product = await cleanup.Products.SingleOrDefaultAsync(item => item.Id == productId);
            if (product is not null) cleanup.Products.Remove(product);
        }
        if (brandId > 0)
        {
            var brand = await cleanup.Brands.SingleOrDefaultAsync(item => item.Id == brandId);
            if (brand is not null) cleanup.Brands.Remove(brand);
        }
        if (categoryId > 0)
        {
            var category = await cleanup.Categories.SingleOrDefaultAsync(item => item.Id == categoryId);
            if (category is not null) cleanup.Categories.Remove(category);
        }
        await cleanup.SaveChangesAsync();
    }

    private sealed class RejectInventoryInsertInterceptor : DbCommandInterceptor
    {
        private bool _productInsertExecuted;

        public override ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            System.Data.Common.DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var containsProductInsert = command.CommandText.Contains("INSERT INTO [products]", StringComparison.OrdinalIgnoreCase);
            if (containsProductInsert)
            {
                _productInsertExecuted = true;
            }

            if (_productInsertExecuted &&
                command.CommandText.Contains("INSERT INTO [inventories]", StringComparison.OrdinalIgnoreCase))
            {
                result.Dispose();
                throw new InventoryInsertRejectedException();
            }

            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class InventoryInsertRejectedException : Exception
    {
    }
}
