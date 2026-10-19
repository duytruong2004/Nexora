using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Admin;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Admin;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Tests;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AdminDashboardSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task DashboardMetrics_MatchCurrentSqlServerRows()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new AppDbContext(options);
        var service = new SqlAdminDashboardQueryService(db);

        var metrics = await service.GetMetricsAsync();
        var expected = new AdminDashboardMetrics(
            await db.Products.CountAsync(),
            await db.Products.CountAsync(product => product.Status == ProductStatuses.Active),
            await db.Categories.CountAsync(),
            await db.UsageNeeds.CountAsync(),
            await db.RecommendationRules.CountAsync());

        Assert.Equal(expected, metrics);
    }
}
