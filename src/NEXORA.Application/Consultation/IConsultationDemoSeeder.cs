namespace NEXORA.Application.Consultation;

public interface IConsultationDemoSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
