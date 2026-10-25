using NEXORA.Application.Consultation;

namespace NEXORA.Tests;

public sealed class ConsultationConfirmationTests
{
    [Fact]
    public async Task AffirmativeReplyToMatchingAssistantClarification_AcceptsOnlyAskedHardConstraints()
    {
        const string requestText = "Laptop dưới 20 triệu để học lập trình, ưu tiên RAM 16 GB và SSD 512 GB";
        const string clarification = "Bạn muốn RAM từ 16 GB trở lên và SSD từ 512 GB trở lên, đúng không?";
        var dataSource = new CapturingDataSource();
        var service = new ConsultationOrchestrator(
            new VietnameseRequirementParser(), new UnavailableAssistant(), dataSource);

        var outcome = await service.ConsultAsync(new ConsultationRequest(
            $"{requestText}\nĐúng",
            PreviousAssistantMessage: clarification,
            IsAffirmativeReply: true));

        Assert.Equal(ConsultationOutcomeKind.NoMatchingProducts, outcome.Kind);
        Assert.Equal(20_000_000m, dataSource.LastQuery?.MaximumPrice);
        Assert.Equal(
            new[] { (1, "GTE", 16m), (2, "GTE", 512m) },
            dataSource.LastQuery?.NumericConstraints);
    }

    [Fact]
    public async Task AffirmativeReplyWithoutMatchingPriorQuestion_DoesNotResolveUncertainConstraint()
    {
        var dataSource = new CapturingDataSource();
        var service = new ConsultationOrchestrator(
            new VietnameseRequirementParser(), new UnavailableAssistant(), dataSource);

        var outcome = await service.ConsultAsync(new ConsultationRequest(
            "Laptop lập trình RAM 16 GB\nĐúng",
            IsAffirmativeReply: true));

        Assert.Equal(ConsultationOutcomeKind.ClarificationRequired, outcome.Kind);
        Assert.Null(dataSource.LastQuery);
    }

    [Fact]
    public async Task ConfirmationDoesNotAcceptIncompatibleSpecificationUnit()
    {
        var dataSource = new CapturingDataSource();
        var service = new ConsultationOrchestrator(
            new VietnameseRequirementParser(), new UnavailableAssistant(), dataSource);

        var outcome = await service.ConsultAsync(new ConsultationRequest(
            "Laptop lập trình RAM 16 kg\nĐúng",
            PreviousAssistantMessage: "Bạn muốn RAM từ 16 kg trở lên, đúng không?",
            IsAffirmativeReply: true));

        Assert.Equal(ConsultationOutcomeKind.ClarificationRequired, outcome.Kind);
        Assert.Null(dataSource.LastQuery);
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
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<ConsultationNeedOption>> GetActiveUsageNeedsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConsultationNeedOption>>([]);

        public Task<ConsultationDataContext?> GetContextAsync(
            string categoryName,
            string usageNeedCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ConsultationDataContext?>(new ConsultationDataContext(
                1,
                categoryName,
                1,
                usageNeedCode,
                "Lập trình",
                [
                    new ConsultationSpecificationDefinition(1, "RAM", "NUMBER", "GB"),
                    new ConsultationSpecificationDefinition(2, "SSD", "NUMBER", "GB")
                ],
                []));

        public Task<IReadOnlyList<ConsultationProduct>> FindCandidatesAsync(
            CandidateQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<ConsultationProduct>>([]);
        }
    }
}
