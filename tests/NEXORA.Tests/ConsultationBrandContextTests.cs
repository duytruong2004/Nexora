using NEXORA.Application.Consultation;

namespace NEXORA.Tests;

public sealed class ConsultationBrandContextTests
{
    [Fact]
    public async Task LatestBrandMentionInBoundedConversationContext_Wins()
    {
        const string conversation = "Laptop ASUS dưới 20 triệu.\nĐổi sang Dell nhé.";
        var source = new CapturingDataSource();
        var service = new ConsultationOrchestrator(new ExplicitLaptopParser(), new UnavailableAssistant(), source);

        await service.ConsultAsync(new ConsultationRequest(conversation));

        Assert.Equal("Dell", source.LastQuery?.BrandName);
    }

    private sealed class ExplicitLaptopParser : IRequirementParser
    {
        public ConsultationParseResult Parse(string? input) => new(true, new RequirementProfile(
            input ?? string.Empty,
            new RecognizedField("Laptop", null, RecognitionState.Explicit),
            new BudgetRequirement(BudgetKind.None, null, null, null, null, RecognitionState.Missing),
            new RecognizedField("GAMING", null, RecognitionState.Explicit),
            [], [], [], [], []), null);
    }

    private sealed class UnavailableAssistant : IConsultationAiAssistant
    {
        public bool IsAvailable => false;
        public Task<ConsultationAiInterpretation?> InterpretAsync(
            string input,
            IReadOnlyList<ConsultationNeedOption> allowedUsageNeeds,
            CancellationToken cancellationToken = default) => Task.FromResult<ConsultationAiInterpretation?>(null);

        public Task<string?> ComposeResponseAsync(
            string input,
            ConsultationOutcome outcome,
            Func<string, CancellationToken, ValueTask>? onChunk = null,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class CapturingDataSource : IConsultationDataSource
    {
        public CandidateQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<string>> GetActiveBrandNamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["ASUS", "Dell"]);

        public Task<IReadOnlyList<ConsultationNeedOption>> GetActiveUsageNeedsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConsultationNeedOption>>([]);

        public Task<ConsultationDataContext?> GetContextAsync(
            string categoryName,
            string usageNeedCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ConsultationDataContext?>(new ConsultationDataContext(
                1, "Laptop", 1, "GAMING", "Chơi game", [], []));

        public Task<IReadOnlyList<ConsultationProduct>> FindCandidatesAsync(
            CandidateQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<ConsultationProduct>>([]);
        }
    }
}
