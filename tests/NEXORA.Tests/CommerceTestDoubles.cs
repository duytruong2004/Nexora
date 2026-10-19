using NEXORA.Application.Commerce;

namespace NEXORA.Tests;

internal sealed class TestCartService : ICartService
{
    public Task<CartSnapshot> GetAsync(int userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CartSnapshot(null, []));

    public Task<int> GetItemCountAsync(int userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<CartMutationResult> AddAsync(
        int userId,
        int productId,
        int quantity,
        CancellationToken cancellationToken = default) => Task.FromResult(CartMutationResult.Success);

    public Task<CartMutationResult> SetQuantityAsync(
        int userId,
        int cartItemId,
        int quantity,
        CancellationToken cancellationToken = default) => Task.FromResult(CartMutationResult.Success);

    public Task<CartMutationResult> RemoveAsync(
        int userId,
        int cartItemId,
        CancellationToken cancellationToken = default) => Task.FromResult(CartMutationResult.Success);
}
