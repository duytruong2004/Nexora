using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NEXORA.Application.Admin;
using NEXORA.Application.Catalog;
using NEXORA.Application.Consultation;
using NEXORA.Application.Commerce;
using NEXORA.Application.Identity;
using NEXORA.Application.Engagement;
using NEXORA.Infrastructure.Catalog;
using NEXORA.Infrastructure.Admin;
using NEXORA.Infrastructure.Consultation;
using NEXORA.Infrastructure.Commerce;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Engagement;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNexoraInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string environmentName,
        string? databaseProfile = null)
    {
        var explicitConnectionString = DatabaseConnectionPolicy.Resolve(connectionString, environmentName, databaseProfile);
        var sqlConnectionString = new SqlConnectionStringBuilder(explicitConnectionString)
        {
            MultipleActiveResultSets = false
        }.ConnectionString;
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(sqlConnectionString, sql =>
                sql.CommandTimeout(30)
                    .EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null)));

        services.AddScoped<ICategoryBrandManagementService, CategoryBrandManagementService>();
        services.AddScoped<IProductManagementService, ProductManagementService>();
        services.AddScoped<IProductImageManagementService, ProductImageManagementService>();
        services.AddScoped<ISpecificationManagementService, SpecificationManagementService>();
        services.AddScoped<IProductSpecificationManagementService, ProductSpecificationManagementService>();
        services.AddScoped<ICustomerCatalogQueryService, CustomerCatalogQueryService>();
        services.AddScoped<IAdminDashboardQueryService, SqlAdminDashboardQueryService>();
        services.AddScoped<IAdminReportingQueryService, SqlAdminReportingQueryService>();
        services.AddScoped<ICatalogDemoSeeder, CatalogDemoSeeder>();
        services.AddSingleton<IRequirementParser, VietnameseRequirementParser>();
        services.AddScoped<IConsultationDataSource, SqlConsultationDataSource>();
        services.AddScoped<IConsultationManagementService, ConsultationManagementService>();
        services.AddScoped<IConsultationService, ConsultationOrchestrator>();
        services.AddScoped<IConsultationDemoSeeder, ConsultationDemoSeeder>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderPricingService, OrderPricingService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<ICustomerOrderService, CustomerOrderService>();
        services.AddScoped<ISalesOrderManagementService, SalesOrderManagementService>();
        services.AddScoped<IOrderCancellationReviewService, OrderCancellationReviewService>();
        services.AddScoped<IWarehouseFulfillmentService, WarehouseFulfillmentService>();
        services.AddScoped<IWarehouseInventoryManagementService, WarehouseInventoryManagementService>();
        services.AddScoped<IDevelopmentInventorySeeder, DevelopmentInventorySeeder>();
        services.AddScoped<IAdminPromotionManagementService, AdminPromotionManagementService>();
        services.AddScoped<IAccountAdministrationService, IdentityAccountAdministrationService>();
        services.AddScoped<IAccountAuthenticationStateReader, AccountAuthenticationStateReader>();
        services.AddScoped<CustomerEngagementService>();
        services.AddScoped<IFavoriteService>(provider => provider.GetRequiredService<CustomerEngagementService>());
        services.AddScoped<IReviewService>(provider => provider.GetRequiredService<CustomerEngagementService>());
        services.AddScoped<IReviewModerationService, ReviewModerationService>();

        return services;
    }
}
