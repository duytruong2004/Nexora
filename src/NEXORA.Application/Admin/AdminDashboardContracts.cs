namespace NEXORA.Application.Admin;

public sealed record AdminDashboardMetrics(
    int TotalProducts,
    int ActiveProducts,
    int TotalCategories,
    int TotalUsageNeeds,
    int TotalRecommendationRules)
{
    public int TotalOrders { get; init; }
    public int PendingOrders { get; init; }
    public int CompletedOrders { get; init; }
    public int ProductsWithoutInventory { get; init; }
}

public interface IAdminDashboardQueryService
{
    Task<AdminDashboardMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
}
