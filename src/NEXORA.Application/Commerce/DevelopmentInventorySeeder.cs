namespace NEXORA.Application.Commerce;

public interface IDevelopmentInventorySeeder
{
    Task SeedMissingAsync(CancellationToken cancellationToken = default);
}
