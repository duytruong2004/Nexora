using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Commerce;

public sealed class CartService(AppDbContext db, IOrderPricingService pricing) : ICartService
{
    public async Task<CartSnapshot> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        EnsureValidUserId(userId);
        var quote = await pricing.GetCurrentQuoteAsync(userId, cancellationToken);
        var lines = quote.Lines.Select(line => new CartLine(
            line.CartItemId,
            line.ProductId,
            line.ProductName,
            line.Sku,
            line.ImageUrl,
            line.UnitPrice,
            line.DiscountAmount,
            line.PromotionName,
            line.Quantity,
            Math.Max(0, line.AvailableQuantity),
            line.IsAvailable)).ToArray();
        return new CartSnapshot(null, lines);
    }

    public async Task<int> GetItemCountAsync(int userId, CancellationToken cancellationToken = default)
    {
        EnsureValidUserId(userId);
        return await db.Carts.AsNoTracking()
            .Where(cart => cart.UserId == userId)
            .SelectMany(cart => cart.Items)
            .Select(item => (int?)item.Quantity)
            .SumAsync(cancellationToken) ?? 0;
    }

    public async Task<CartMutationResult> AddAsync(
        int userId,
        int productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        EnsureValidUserId(userId);
        if (productId <= 0 || quantity <= 0)
        {
            return new CartMutationResult(CartMutationFailure.InvalidQuantity);
        }

        var product = await db.Products.AsNoTracking()
            .Where(item => item.Id == productId && item.Status == ProductStatuses.Active &&
                           item.Category.IsActive && item.Brand.IsActive)
            .Select(item => new
            {
                item.Id,
                Available = item.Inventory == null
                    ? 0
                    : item.Inventory.QuantityInStock - item.Inventory.ReservedQuantity
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null)
        {
            return new CartMutationResult(CartMutationFailure.ProductUnavailable);
        }

        if (product.Available <= 0)
        {
            return new CartMutationResult(CartMutationFailure.OutOfStock);
        }

        if (product.Available < quantity)
        {
            return new CartMutationResult(CartMutationFailure.InsufficientStock);
        }

        var cart = await GetOrCreateCartAsync(userId, cancellationToken);
        var existing = await db.CartItems.AsNoTracking().SingleOrDefaultAsync(
            item => item.CartId == cart.Id && item.ProductId == productId,
            cancellationToken);

        if (existing is not null)
        {
            return await IncrementExistingAsync(cart.Id, existing, quantity, product.Available, cancellationToken);
        }

        var newItem = new CartItem
        {
            CartId = cart.Id,
            ProductId = productId,
            Quantity = quantity,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.CartItems.Add(newItem);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return CartMutationResult.Success;
        }
        catch (DbUpdateException)
        {
            db.Entry(newItem).State = EntityState.Detached;
            var racedItem = await db.CartItems.SingleOrDefaultAsync(
                item => item.CartId == cart.Id && item.ProductId == productId,
                cancellationToken);
            if (racedItem is null)
            {
                return new CartMutationResult(CartMutationFailure.ConcurrentUpdate);
            }

            return await IncrementExistingAsync(cart.Id, racedItem, quantity, product.Available, cancellationToken);
        }
    }

    public async Task<CartMutationResult> SetQuantityAsync(
        int userId,
        int cartItemId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        EnsureValidUserId(userId);
        if (quantity <= 0)
        {
            return new CartMutationResult(CartMutationFailure.InvalidQuantity);
        }

        var line = await db.CartItems.AsNoTracking()
            .Where(item => item.Id == cartItemId && item.Cart.UserId == userId)
            .Select(item => new
            {
                item.Id,
                item.CartId,
                IsProductActive = item.Product.Status == ProductStatuses.Active &&
                                  item.Product.Category.IsActive && item.Product.Brand.IsActive,
                Available = item.Product.Inventory == null
                    ? 0
                    : item.Product.Inventory.QuantityInStock - item.Product.Inventory.ReservedQuantity
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (line is null)
        {
            return new CartMutationResult(CartMutationFailure.ItemNotFound);
        }

        if (!line.IsProductActive)
        {
            return new CartMutationResult(CartMutationFailure.ProductUnavailable);
        }

        if (line.Available <= 0)
        {
            return new CartMutationResult(CartMutationFailure.OutOfStock);
        }

        if (quantity > line.Available)
        {
            return new CartMutationResult(CartMutationFailure.InsufficientStock);
        }

        var updated = await db.CartItems
            .Where(item => item.Id == line.Id && item.CartId == line.CartId && item.Cart.UserId == userId &&
                           item.Product.Status == ProductStatuses.Active &&
                           item.Product.Category.IsActive && item.Product.Brand.IsActive &&
                           item.Product.Inventory != null &&
                           quantity <= item.Product.Inventory.QuantityInStock - item.Product.Inventory.ReservedQuantity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Quantity, quantity)
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow), cancellationToken);
        if (updated == 1) return CartMutationResult.Success;

        var current = await db.CartItems.AsNoTracking()
            .Where(item => item.Id == line.Id && item.Cart.UserId == userId)
            .Select(item => new
            {
                IsProductActive = item.Product.Status == ProductStatuses.Active &&
                                  item.Product.Category.IsActive && item.Product.Brand.IsActive,
                item.Quantity,
                Available = item.Product.Inventory == null
                    ? 0
                    : item.Product.Inventory.QuantityInStock - item.Product.Inventory.ReservedQuantity
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (current is not null && !current.IsProductActive)
        {
            return new CartMutationResult(CartMutationFailure.ProductUnavailable);
        }

        if (current is null)
        {
            return new CartMutationResult(CartMutationFailure.ConcurrentUpdate);
        }

        if (current.Available <= 0)
        {
            return new CartMutationResult(CartMutationFailure.OutOfStock);
        }

        return quantity > current.Available
            ? new CartMutationResult(CartMutationFailure.InsufficientStock)
            : new CartMutationResult(CartMutationFailure.ConcurrentUpdate);
    }

    public async Task<CartMutationResult> RemoveAsync(
        int userId,
        int cartItemId,
        CancellationToken cancellationToken = default)
    {
        EnsureValidUserId(userId);
        var removed = await db.CartItems
            .Where(item => item.Id == cartItemId && item.Cart.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        return removed == 1
            ? CartMutationResult.Success
            : new CartMutationResult(CartMutationFailure.ItemNotFound);
    }

    private async Task<Cart> GetOrCreateCartAsync(int userId, CancellationToken cancellationToken)
    {
        var cart = await db.Carts.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (cart is not null)
        {
            return cart;
        }

        cart = new Cart { UserId = userId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Carts.Add(cart);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return cart;
        }
        catch (DbUpdateException)
        {
            db.Entry(cart).State = EntityState.Detached;
            var createdByConcurrentRequest = await db.Carts.SingleOrDefaultAsync(
                item => item.UserId == userId,
                cancellationToken);
            return createdByConcurrentRequest ?? throw new InvalidOperationException(
                "The cart could not be created because the database rejected the insert.");
        }
    }

    private async Task<CartMutationResult> IncrementExistingAsync(
        int cartId,
        CartItem tracked,
        int addedQuantity,
        int availableQuantity,
        CancellationToken cancellationToken)
    {
        if (availableQuantity < addedQuantity || tracked.Quantity > availableQuantity - addedQuantity)
        {
            return new CartMutationResult(CartMutationFailure.InsufficientStock);
        }

        var itemId = tracked.Id;
        var changed = await db.CartItems
            .Where(item => item.Id == itemId && item.CartId == cartId &&
                           item.Product.Status == ProductStatuses.Active &&
                           item.Product.Category.IsActive && item.Product.Brand.IsActive &&
                           item.Quantity <= availableQuantity - addedQuantity &&
                           item.Product.Inventory != null &&
                           item.Quantity + addedQuantity <= item.Product.Inventory.QuantityInStock - item.Product.Inventory.ReservedQuantity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Quantity, item => item.Quantity + addedQuantity)
                .SetProperty(item => item.UpdatedAt, DateTime.UtcNow), cancellationToken);
        if (changed == 1) return CartMutationResult.Success;

        var current = await db.CartItems.AsNoTracking()
            .Where(item => item.Id == itemId && item.CartId == cartId)
            .Select(item => new
            {
                item.Quantity,
                IsProductActive = item.Product.Status == ProductStatuses.Active &&
                                  item.Product.Category.IsActive && item.Product.Brand.IsActive,
                Available = item.Product.Inventory == null
                    ? 0
                    : item.Product.Inventory.QuantityInStock - item.Product.Inventory.ReservedQuantity
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (current is not null && !current.IsProductActive)
        {
            return new CartMutationResult(CartMutationFailure.ProductUnavailable);
        }

        if (current is null)
        {
            return new CartMutationResult(CartMutationFailure.ConcurrentUpdate);
        }

        if (current.Available <= 0)
        {
            return new CartMutationResult(CartMutationFailure.OutOfStock);
        }

        return current.Quantity > current.Available || addedQuantity > current.Available - current.Quantity
            ? new CartMutationResult(CartMutationFailure.InsufficientStock)
            : new CartMutationResult(CartMutationFailure.ConcurrentUpdate);
    }

    private static void EnsureValidUserId(int userId)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }
    }
}
