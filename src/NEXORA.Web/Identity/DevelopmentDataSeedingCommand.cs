using NEXORA.Application.Catalog;
using NEXORA.Application.Commerce;
using NEXORA.Application.Consultation;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Web.Identity;

public static class DevelopmentSeedPolicy
{
    public static bool IsAllowed(
        string? environmentName,
        string? databaseProfile,
        string? connectionString) =>
        DatabaseConnectionPolicy.IsPersonalDevelopmentTarget(connectionString, databaseProfile, environmentName);
}

public static class DevelopmentDataSeedingCommand
{
    public static async Task<int> ExecuteAsync(
        IServiceProvider services,
        string? connectionString,
        string? databaseProfile,
        string? environmentName,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!DevelopmentSeedPolicy.IsAllowed(environmentName, databaseProfile, connectionString))
        {
            logger.LogError("Development data seeding was refused because the target is not a personal local Development database.");
            Console.WriteLine("Từ chối seed: chỉ chạy trên Development với Database:Profile=PersonalDevelopment và NEXORA_Dev_<Member> cục bộ.");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<RoleSeeder>().EnsureRolesAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<ICatalogDemoSeeder>().SeedAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IConsultationDemoSeeder>().SeedAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IDevelopmentInventorySeeder>().SeedMissingAsync(cancellationToken);

            Console.WriteLine("Đã kiểm tra bốn role chuẩn và bổ sung dữ liệu demo còn thiếu. Các bản ghi khớp và tồn kho hiện có được giữ nguyên.");
            logger.LogInformation("Explicit personal Development data seed completed.");
            return 0;
        }
        catch (Exception exception)
        {
            logger.LogError("Personal Development data seeding failed. ExceptionType={ExceptionType}.", exception.GetType().Name);
            Console.WriteLine("Seed thất bại. Không có migration tự động; kiểm tra database đã được migrate và xem log không chứa connection string.");
            return 3;
        }
    }
}
