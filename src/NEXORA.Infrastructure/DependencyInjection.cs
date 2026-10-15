using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NEXORA.Application.Catalog;
using NEXORA.Infrastructure.Catalog;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNexoraInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
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
        services.AddScoped<ICatalogDemoSeeder, CatalogDemoSeeder>();

        return services;
    }
}
