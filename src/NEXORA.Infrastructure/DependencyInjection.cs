using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NEXORA.Application.Admin;
using NEXORA.Application.Catalog;
using NEXORA.Application.Consultation;
using NEXORA.Application.Commerce;
using NEXORA.Infrastructure.Catalog;
using NEXORA.Infrastructure.Admin;
using NEXORA.Infrastructure.Consultation;
using NEXORA.Infrastructure.Commerce;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNexoraInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        var sqlConnectionString = new SqlConnectionStringBuilder(connectionString)
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

        return services;
    }
}
