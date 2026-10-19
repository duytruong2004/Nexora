using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Commerce;

public sealed class DevelopmentInventorySeeder(AppDbContext db, ILogger<DevelopmentInventorySeeder> logger)
    : IDevelopmentInventorySeeder
{
    private const int SimulatedStartingQuantity = 10;
    private static readonly string[] DemoProductSkus =
    [
        "NXD-LAP-001", "NXD-LAP-002", "NXD-LAP-003",
        "NXD-PHN-001", "NXD-PHN-002", "NXD-PHN-003",
        "NXD-TAB-001", "NXD-TAB-002", "NXD-TAB-003",
        "NXD-ACC-001", "NXD-ACC-002"
    ];

    public async Task SeedMissingAsync(CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var activeProductIds = await db.Products.AsNoTracking()
                .Where(product => DemoProductSkus.Contains(product.Sku) &&
                                  product.Status == ProductStatuses.Active &&
                                  product.Category.IsActive && product.Brand.IsActive)
                .Select(product => product.Id)
                .ToListAsync(cancellationToken);
            var existingIds = await db.Inventories.AsNoTracking()
                .Where(inventory => activeProductIds.Contains(inventory.ProductId))
                .Select(inventory => inventory.ProductId)
                .ToHashSetAsync(cancellationToken);
            var added = 0;

            foreach (var productId in activeProductIds.Where(id => !existingIds.Contains(id)))
            {
                var inventory = new Inventory { ProductId = productId };
                inventory.Receive(SimulatedStartingQuantity);
                db.Inventories.Add(inventory);
                added++;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Development inventory seed completed. Added {AddedCount} missing product records with simulated stock.",
                added);
        });
    }
}
