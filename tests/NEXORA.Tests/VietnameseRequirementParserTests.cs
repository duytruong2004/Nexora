using NEXORA.Application.Consultation;

namespace NEXORA.Tests;

public sealed class VietnameseRequirementParserTests
{
    private readonly VietnameseRequirementParser _parser = new();

    [Fact]
    public void TcNlp01_ExtractsLaptopBudgetProgrammingAndTwoSoftPreferences()
    {
        var result = _parser.Parse("Laptop dưới 25 triệu, lập trình, ưu tiên RAM và pin tốt");

        Assert.True(result.IsValid);
        Assert.Equal("Laptop", result.Profile.Category.Value);
        Assert.Equal(BudgetKind.Maximum, result.Profile.Budget.Kind);
        Assert.Equal(25_000_000m, result.Profile.Budget.MaximumVnd);
        Assert.Equal("PROGRAMMING", result.Profile.UsageNeed.Value);
        Assert.Contains(result.Profile.SoftPreferences, item => item.SpecificationName == "RAM");
        Assert.Contains(result.Profile.SoftPreferences, item => item.SpecificationName == "Dung lượng pin");
        Assert.Empty(result.Profile.MissingFields);
    }

    [Theory]
    [InlineData("Laptop khoảng 20tr học online", BudgetKind.Approximate, 20_000_000)]
    [InlineData("Laptop 20 triệu học trực tuyến", BudgetKind.Approximate, 20_000_000)]
    [InlineData("Laptop ngân sách 20.000.000đ học tập", BudgetKind.Maximum, 20_000_000)]
    public void TcNlp02_NormalizesEquivalentVietnameseMoney(
        string input,
        BudgetKind expectedKind,
        decimal expectedVnd)
    {
        var result = _parser.Parse(input);

        Assert.True(result.IsValid);
        Assert.Equal(expectedKind, result.Profile.Budget.Kind);
        Assert.Equal(expectedVnd, result.Profile.Budget.TargetVnd ?? result.Profile.Budget.MaximumVnd);
        Assert.Equal("STUDY", result.Profile.UsageNeed.Value);
    }

    [Fact]
    public void BudgetRange_IsPreservedAsTwoHardBounds()
    {
        var result = _parser.Parse("Laptop từ 15 đến 20 triệu học tập");

        Assert.Equal(BudgetKind.Range, result.Profile.Budget.Kind);
        Assert.Equal(15_000_000m, result.Profile.Budget.MinimumVnd);
        Assert.Equal(20_000_000m, result.Profile.Budget.MaximumVnd);
    }

    [Fact]
    public void TcNlp03_AsksForDeviceAndClarifiesVagueWorkPurpose()
    {
        var result = _parser.Parse("Tôi cần máy cho công việc");

        Assert.True(result.IsValid);
        Assert.Null(result.Profile.Category.Value);
        Assert.Equal(RecognitionState.Uncertain, result.Profile.UsageNeed.State);
        Assert.Contains("danh mục", result.Profile.MissingFields);
        Assert.Contains("mục đích sử dụng", result.Profile.MissingFields);
        Assert.NotEmpty(result.Profile.ClarificationQuestions);
        Assert.Equal(BudgetKind.None, result.Profile.Budget.Kind);
    }

    [Fact]
    public void ExplicitNumericSpecificationConstraint_IsNormalizedWithUnit()
    {
        var result = _parser.Parse("Laptop lập trình RAM tối thiểu 16 GB");
        var constraint = Assert.Single(result.Profile.HardConstraints);

        Assert.Equal("RAM", constraint.SpecificationName);
        Assert.Equal("GTE", constraint.Operator);
        Assert.Equal(16m, constraint.Value);
        Assert.Equal("GB", constraint.Unit);
        Assert.Equal(RecognitionState.Normalized, constraint.State);
    }

    [Fact]
    public void NumericSpecificationWithoutExplicitOperator_RemainsUncertain()
    {
        var result = _parser.Parse("Laptop lập trình RAM 16GB");

        Assert.Equal(RecognitionState.Uncertain, Assert.Single(result.Profile.HardConstraints).State);
        Assert.Contains(result.Profile.ClarificationQuestions, question => question.Contains("RAM", StringComparison.Ordinal));
    }

    [Fact]
    public void IncompatibleSpecificationUnit_IsNotSilentlyApplied()
    {
        var result = _parser.Parse("Laptop lập trình RAM tối thiểu 16 kg");
        var constraint = Assert.Single(result.Profile.HardConstraints);

        Assert.Equal(RecognitionState.Uncertain, constraint.State);
        Assert.Contains(result.Profile.Warnings, warning => warning.Contains("không tương thích", StringComparison.Ordinal));
    }

    [Fact]
    public void ContradictoryHardConstraintsRequireClarification()
    {
        var result = _parser.Parse("Laptop lập trình RAM tối thiểu 16GB, RAM tối đa 8GB");

        Assert.Contains(result.Profile.Warnings, warning => warning.Contains("mâu thuẫn", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(result.Profile.ClarificationQuestions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!! ???")]
    public void TcNlp08_EmptyOrNonsensicalInputIsRejected(string input)
    {
        var result = _parser.Parse(input);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Profile.HardConstraints);
    }

    [Fact]
    public void InputLongerThanConfiguredLimit_IsRejected()
    {
        var result = _parser.Parse(new string('x', 1201));

        Assert.False(result.IsValid);
        Assert.Contains("1200", result.Error, StringComparison.Ordinal);
    }
}
