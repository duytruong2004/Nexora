using NEXORA.Application.Consultation;
using NEXORA.Domain.Entities;

namespace NEXORA.Tests;

public sealed class ConsultationScoringTests
{
    [Fact]
    public void RequiredRulesFilterBeforeWeightedRanking()
    {
        var context = Context(
            Rule(1, "RAM", "NUMBER", "GB", "GTE", "16", 2m, required: true),
            Rule(2, "SSD", "NUMBER", "GB", "GTE", "512", 1m));
        var products = new[]
        {
            Product(1, 20_000_000m, Spec(1, "RAM", "NUMBER", "GB", "8", 8m), Spec(2, "SSD", "NUMBER", "GB", "512", 512m)),
            Product(2, 22_000_000m, Spec(1, "RAM", "NUMBER", "GB", "16", 16m), Spec(2, "SSD", "NUMBER", "GB", "512", 512m)),
            Product(3, 25_000_000m, Spec(1, "RAM", "NUMBER", "GB", "32", 32m), Spec(2, "SSD", "NUMBER", "GB", "1024", 1024m))
        };

        var ranked = RecommendationScoringEngine.Rank(context, products, Profile());

        Assert.Equal([2, 3], ranked.Select(item => item.Product.Id));
        Assert.All(ranked, item => Assert.Contains(item.Reasons, reason => reason.Contains("RAM", StringComparison.Ordinal)));
    }

    [Fact]
    public void TcNlp05_RankingIsStableAcrossRepeatedRuns()
    {
        var context = Context(
            Rule(1, "RAM", "NUMBER", "GB", "GTE", "16", 2m),
            Rule(2, "SSD", "NUMBER", "GB", "GTE", "512", 1m));
        var products = new[]
        {
            Product(11, 20_000_000m, Spec(1, "RAM", "NUMBER", "GB", "32", 32m), Spec(2, "SSD", "NUMBER", "GB", "512", 512m)),
            Product(12, 21_000_000m, Spec(1, "RAM", "NUMBER", "GB", "16", 16m), Spec(2, "SSD", "NUMBER", "GB", "1024", 1024m))
        };

        var first = RecommendationScoringEngine.Rank(context, products, Profile()).Select(item => item.Product.Id).ToArray();
        var second = RecommendationScoringEngine.Rank(context, products, Profile()).Select(item => item.Product.Id).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void TcNlp07_ChangingDatabaseWeightsChangesRankingOnly()
    {
        var products = new[]
        {
            Product(1, 20_000_000m, Spec(1, "RAM", "NUMBER", "GB", "32", 32m), Spec(2, "SSD", "NUMBER", "GB", "256", 256m)),
            Product(2, 20_000_000m, Spec(1, "RAM", "NUMBER", "GB", "8", 8m), Spec(2, "SSD", "NUMBER", "GB", "1024", 1024m))
        };
        var favorStorage = Context(
            Rule(1, "RAM", "NUMBER", "GB", "GTE", "32", 1m),
            Rule(2, "SSD", "NUMBER", "GB", "GTE", "1024", 3m));
        var favorMemory = Context(
            Rule(1, "RAM", "NUMBER", "GB", "GTE", "32", 3m),
            Rule(2, "SSD", "NUMBER", "GB", "GTE", "1024", 1m));
        var priceBefore = products.Select(item => item.Price).ToArray();

        var storageRanking = RecommendationScoringEngine.Rank(favorStorage, products, Profile());
        var memoryRanking = RecommendationScoringEngine.Rank(favorMemory, products, Profile());

        Assert.Equal(2, storageRanking[0].Product.Id);
        Assert.Equal(1, memoryRanking[0].Product.Id);
        Assert.Equal(priceBefore, products.Select(item => item.Price));
    }

    [Fact]
    public void TcNlp06_ExplanationAndLimitationsUseActualSpecificationData()
    {
        var context = Context(Rule(1, "Trọng lượng", "NUMBER", "kg", "LTE", "1.8", 1m));
        var product = Product(1, 20_000_000m, Spec(1, "Trọng lượng", "NUMBER", "kg", "1.45", 1.45m, "1,45 kg"));

        var result = Assert.Single(RecommendationScoringEngine.Rank(context, [product], Profile()));

        Assert.Contains(result.Reasons, reason => reason.Contains("1,45 kg", StringComparison.Ordinal));
        Assert.Contains(result.Reasons, reason => reason.Contains("≤ 1.8 kg", StringComparison.Ordinal));
        Assert.Equal("1,45 kg", Assert.Single(result.Criteria).ProductValue);
    }

    [Fact]
    public void MissingCategorySpecificationIsReportedInsteadOfInventingAWeightRule()
    {
        var context = Context();
        var profile = Profile() with
        {
            SoftPreferences = [new SoftSpecificationPreference(
                "Dung lượng pin", PreferenceDirection.Higher, "pin tốt", RecognitionState.Normalized)]
        };

        var result = Assert.Single(RecommendationScoringEngine.Rank(context, [Product(1, 20_000_000m)], profile));

        Assert.Contains(result.Limitations, limitation => limitation.Contains("Laptop chưa khai báo thông số Dung lượng pin", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Limitations, limitation => limitation.Contains("quy tắc trọng số", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Reasons, reason => reason.Contains("thời lượng", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingSoftValueDoesNotEarnScoreOrInventReason()
    {
        var context = Context(Rule(1, "RAM", "NUMBER", "GB", "GTE", "16", 4m));
        var product = Product(1, 10_000_000m);

        var result = Assert.Single(RecommendationScoringEngine.Rank(context, [product], Profile()));

        Assert.Equal(0m, result.Score);
        Assert.Contains(result.Limitations, limitation => limitation.Contains("RAM", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Reasons, reason => reason.Contains("16 GB", StringComparison.Ordinal));
    }

    [Fact]
    public void ZeroTotalWeightUsesStablePriceThenIdFallback()
    {
        var context = Context(Rule(1, "RAM", "NUMBER", "GB", "GTE", "16", 0m));
        var products = new[]
        {
            Product(4, 12_000_000m, Spec(1, "RAM", "NUMBER", "GB", "16", 16m)),
            Product(3, 10_000_000m, Spec(1, "RAM", "NUMBER", "GB", "16", 16m)),
            Product(2, 10_000_000m, Spec(1, "RAM", "NUMBER", "GB", "16", 16m))
        };

        var ranked = RecommendationScoringEngine.Rank(context, products, Profile());

        Assert.Equal([2, 3, 4], ranked.Select(item => item.Product.Id));
        Assert.All(ranked, item => Assert.Equal(0m, item.Score));
    }

    private static ConsultationDataContext Context(params ConsultationRuleData[] rules) => new(
        1, "Laptop", 1, "PROGRAMMING", "Lập trình",
        [new ConsultationSpecificationDefinition(1, "RAM", "NUMBER", "GB"), new ConsultationSpecificationDefinition(2, "SSD", "NUMBER", "GB")],
        rules);

    private static ConsultationRuleData Rule(int id, string name, string type, string? unit, string op, string threshold, decimal weight, bool required = false) =>
        new(id, id, name, type, unit, op, threshold, weight, required);

    private static ConsultationProduct Product(int id, decimal price, params ConsultationSpecificationValue[] specifications) =>
        new(id, $"Test laptop {id}", $"T-{id}", 1, "Laptop", "Test", price, null, null, null, specifications);

    private static ConsultationSpecificationValue Spec(int id, string name, string type, string? unit, string value, decimal? numeric, string? display = null) =>
        new(id, name, type, unit, value, numeric, display);

    private static RequirementProfile Profile() => new(
        "Laptop lập trình", new RecognizedField("Laptop", null, RecognitionState.Explicit),
        new BudgetRequirement(BudgetKind.None, null, null, null, null, RecognitionState.Missing),
        new RecognizedField("PROGRAMMING", null, RecognitionState.Explicit), [], [], [], [], []);
}
