using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NEXORA.Application.Commerce;
using NEXORA.Infrastructure.Identity;
using NEXORA.Web.Controllers;

namespace NEXORA.Tests;

public sealed class CartControllerAddResponseTests
{
    [Fact]
    public async Task Add_JsonRequest_ReturnsServerCartCountAfterSuccessfulMutation()
    {
        var carts = new StubCartService(CartMutationResult.Success, itemCount: 13);
        var controller = CreateController(carts);

        var result = await controller.Add(11, quantity: 1, returnUrl: "/Catalog", CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal("Đã thêm vào giỏ hàng.", json.Value!.GetType().GetProperty("message")!.GetValue(json.Value));
        Assert.Equal(13, json.Value.GetType().GetProperty("itemCount")!.GetValue(json.Value));
        Assert.Equal(42, carts.LastUserId);
        Assert.Equal(11, carts.LastProductId);
        Assert.Equal(1, carts.LastQuantity);
    }

    [Fact]
    public async Task Add_OutOfStockReturnsSpecificVietnameseMessage()
    {
        var controller = CreateController(new StubCartService(
            new CartMutationResult(CartMutationFailure.OutOfStock), itemCount: 0));

        var result = await controller.Add(11, quantity: 1, returnUrl: "/Catalog", CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("Sản phẩm hiện đã hết hàng.", response.Value!.GetType().GetProperty("message")!.GetValue(response.Value));
    }

    [Theory]
    [InlineData(CartMutationFailure.InvalidQuantity, StatusCodes.Status400BadRequest)]
    [InlineData(CartMutationFailure.ProductUnavailable, StatusCodes.Status404NotFound)]
    [InlineData(CartMutationFailure.OutOfStock, StatusCodes.Status409Conflict)]
    [InlineData(CartMutationFailure.InsufficientStock, StatusCodes.Status409Conflict)]
    [InlineData(CartMutationFailure.ConcurrentUpdate, StatusCodes.Status409Conflict)]
    public async Task Add_JsonRequest_ReturnsFailureWithoutReadingOrInventingBadgeCount(
        CartMutationFailure failure,
        int expectedStatusCode)
    {
        var carts = new StubCartService(new CartMutationResult(failure), itemCount: 13);
        var controller = CreateController(carts);

        var result = await controller.Add(11, quantity: 1, returnUrl: "/Catalog", CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Equal(0, carts.ItemCountReadCount);
    }

    [Fact]
    public async Task Update_JsonRequest_ReturnsServerSnapshotAfterSuccessfulMutation()
    {
        var snapshot = CreateSnapshot(quantity: 3);
        var carts = new StubCartService(CartMutationResult.Success, itemCount: 13, snapshot: snapshot);
        var controller = CreateController(carts);

        var result = await controller.Update(7, quantity: 3, cancellationToken: CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(42, carts.LastUserId);
        Assert.Equal(7, carts.LastCartItemId);
        Assert.Equal(3, carts.LastQuantity);
        Assert.Equal(1, carts.SnapshotReadCount);
        var cart = json.Value!.GetType().GetProperty("cart")!.GetValue(json.Value)!;
        Assert.Equal(300m, (decimal)cart.GetType().GetProperty("subtotal")!.GetValue(cart)!);
        var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            cart.GetType().GetProperty("items")!.GetValue(cart));
        var line = items.Cast<object>().Single();
        Assert.Equal(3, (int)line.GetType().GetProperty("quantity")!.GetValue(line)!);
    }

    [Fact]
    public async Task Update_JsonStockFailure_ReturnsCurrentServerSnapshotAndConflictStatus()
    {
        var snapshot = CreateSnapshot(quantity: 2);
        var carts = new StubCartService(
            CartMutationResult.Success,
            itemCount: 13,
            updateResult: new CartMutationResult(CartMutationFailure.InsufficientStock),
            snapshot: snapshot);
        var controller = CreateController(carts);

        var result = await controller.Update(7, quantity: 9, cancellationToken: CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal(1, carts.SnapshotReadCount);
        var cart = response.Value!.GetType().GetProperty("cart")!.GetValue(response.Value)!;
        var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            cart.GetType().GetProperty("items")!.GetValue(cart));
        var line = items.Cast<object>().Single();
        Assert.Equal(2, (int)line.GetType().GetProperty("quantity")!.GetValue(line)!);
    }

    private static CartSnapshot CreateSnapshot(int quantity) => new(null,
    [
        new CartLine(7, 11, "Demo product", "SKU-1", null, 100m, 0m, null, quantity, 4, true)
    ]);

    private static CartController CreateController(StubCartService carts)
    {
        var userManager = new UserManager<AppUser>(
            new NoOpUserStore(),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<AppUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<AppUser>>.Instance);
        var controller = new CartController(carts, userManager);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "42")],
                authenticationType: "test"))
        };
        context.Request.Headers["Accept"] = "application/json";
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private sealed class StubCartService(
        CartMutationResult addResult,
        int itemCount,
        CartMutationResult? updateResult = null,
        CartSnapshot? snapshot = null) : ICartService
    {
        private readonly CartMutationResult _updateResult = updateResult ?? CartMutationResult.Success;
        private readonly CartSnapshot _snapshot = snapshot ?? new CartSnapshot(null, []);
        public int LastUserId { get; private set; }
        public int LastProductId { get; private set; }
        public int LastQuantity { get; private set; }
        public int ItemCountReadCount { get; private set; }
        public int LastCartItemId { get; private set; }
        public int SnapshotReadCount { get; private set; }

        public Task<CartMutationResult> AddAsync(
            int userId,
            int productId,
            int quantity,
            CancellationToken cancellationToken = default)
        {
            LastUserId = userId;
            LastProductId = productId;
            LastQuantity = quantity;
            return Task.FromResult(addResult);
        }

        public Task<int> GetItemCountAsync(int userId, CancellationToken cancellationToken = default)
        {
            ItemCountReadCount++;
            return Task.FromResult(itemCount);
        }

        public Task<CartSnapshot> GetAsync(int userId, CancellationToken cancellationToken = default)
        {
            LastUserId = userId;
            SnapshotReadCount++;
            return Task.FromResult(_snapshot);
        }

        public Task<CartMutationResult> SetQuantityAsync(
            int userId,
            int cartItemId,
            int quantity,
            CancellationToken cancellationToken = default)
        {
            LastUserId = userId;
            LastCartItemId = cartItemId;
            LastQuantity = quantity;
            return Task.FromResult(_updateResult);
        }

        public Task<CartMutationResult> RemoveAsync(
            int userId,
            int cartItemId,
            CancellationToken cancellationToken = default) => Task.FromResult(CartMutationResult.Success);
    }

    private sealed class NoOpUserStore : IUserStore<AppUser>
    {
        public Task<IdentityResult> CreateAsync(AppUser user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentityResult> UpdateAsync(AppUser user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentityResult> DeleteAsync(AppUser user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AppUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AppUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> GetUserIdAsync(AppUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public Task<string?> GetUserNameAsync(AppUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetUserNameAsync(AppUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(AppUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(AppUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
