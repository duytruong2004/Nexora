namespace NEXORA.Application.Admin;

public sealed record AdminDashboardMetrics(
    int TotalProducts,
    int ActiveProducts,
    int TotalCategories,
    int TotalUsageNeeds,
    int TotalRecommendationRules);

public interface IAdminDashboardQueryService
{
    Task<AdminDashboardMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
}
