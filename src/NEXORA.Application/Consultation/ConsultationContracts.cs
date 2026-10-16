namespace NEXORA.Application.Consultation;

public enum RecognitionState
{
    Explicit,
    Normalized,
    Uncertain,
    Missing
}

public enum BudgetKind
{
    None,
    Maximum,
    Range,
    Approximate
}

public enum PreferenceDirection
{
    Higher,
    Lower,
    Categorical
}

public sealed record RecognizedField(
    string? Value,
    string? OriginalText,
    RecognitionState State);

public sealed record BudgetRequirement(
    BudgetKind Kind,
    decimal? MinimumVnd,
    decimal? MaximumVnd,
    decimal? TargetVnd,
    string? OriginalText,
    RecognitionState State);

public sealed record HardSpecificationConstraint(
    string SpecificationName,
    string Operator,
    decimal Value,
    string Unit,
    string OriginalText,
    RecognitionState State);

public sealed record SoftSpecificationPreference(
    string SpecificationName,
    PreferenceDirection Direction,
    string OriginalText,
    RecognitionState State);

public sealed record RequirementProfile(
    string Input,
    RecognizedField Category,
    BudgetRequirement Budget,
    RecognizedField UsageNeed,
    IReadOnlyList<HardSpecificationConstraint> HardConstraints,
    IReadOnlyList<SoftSpecificationPreference> SoftPreferences,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> ClarificationQuestions,
    IReadOnlyList<string> Warnings);

public sealed record ConsultationParseResult(bool IsValid, RequirementProfile Profile, string? Error);

public interface IRequirementParser
{
    ConsultationParseResult Parse(string? input);
}

public sealed record ConsultationRequest(string? Description, string? ConfirmedCategory = null, string? ConfirmedUsage = null);

public sealed record ConsultationProduct(
    int Id,
    string Name,
    string Sku,
    int CategoryId,
    string CategoryName,
    string BrandName,
    decimal Price,
    decimal? DiscountPrice,
    string? PrimaryImageUrl,
    string? Description,
    IReadOnlyList<ConsultationSpecificationValue> Specifications);

public sealed record ConsultationSpecificationDefinition(
    int Id,
    string Name,
    string DataType,
    string? Unit);

public sealed record ConsultationRuleData(
    int Id,
    int SpecificationId,
    string SpecificationName,
    string DataType,
    string? Unit,
    string Operator,
    string ThresholdValue,
    decimal Weight,
    bool IsRequired);

public sealed record ConsultationDataContext(
    int CategoryId,
    string CategoryName,
    int UsageNeedId,
    string UsageNeedCode,
    string UsageNeedName,
    IReadOnlyList<ConsultationSpecificationDefinition> Specifications,
    IReadOnlyList<ConsultationRuleData> Rules);

public sealed record ConsultationNeedOption(string Code, string Name);

public sealed record CandidateQuery(
    int CategoryId,
    decimal? MinimumPrice,
    decimal? MaximumPrice,
    IReadOnlyList<(int SpecificationId, string Operator, decimal Value)> NumericConstraints);

public interface IConsultationDataSource
{
    Task<IReadOnlyList<ConsultationNeedOption>> GetActiveUsageNeedsAsync(CancellationToken cancellationToken = default);

    Task<ConsultationDataContext?> GetContextAsync(
        string categoryName,
        string usageNeedCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConsultationProduct>> FindCandidatesAsync(
        CandidateQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record ConsultationSpecificationValue(
    int SpecificationId,
    string Name,
    string DataType,
    string? Unit,
    string Value,
    decimal? NumericValue,
    string? DisplayValue);

public sealed record RecommendationCriterionResult(
    string SpecificationName,
    decimal Weight,
    decimal? Score,
    bool IsRequired,
    string? ProductValue,
    string Operator,
    string ThresholdValue,
    string? Explanation);

public sealed record RecommendedProduct(
    ConsultationProduct Product,
    decimal Score,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<RecommendationCriterionResult> Criteria);

public enum ConsultationOutcomeKind
{
    InvalidInput,
    ClarificationRequired,
    NoMatchingProducts,
    Results
}

public sealed record ConsultationOutcome(
    ConsultationOutcomeKind Kind,
    RequirementProfile Profile,
    IReadOnlyList<RecommendedProduct> Recommendations,
    string Message,
    long ElapsedMilliseconds = 0);

public interface IConsultationService
{
    Task<ConsultationOutcome> ConsultAsync(ConsultationRequest request, CancellationToken cancellationToken = default);
}

public sealed record UsageNeedAdminRow(int Id, string Code, string Name, string? Description, bool IsActive, int RuleCount);
public sealed record UsageNeedInput(int? Id, string Code, string Name, string? Description, bool IsActive);
public sealed record RecommendationRuleAdminRow(
    int Id,
    int UsageNeedId,
    string UsageNeedName,
    int CategoryId,
    string CategoryName,
    int SpecificationId,
    string SpecificationName,
    string? Unit,
    string DataType,
    string Operator,
    string ThresholdValue,
    decimal Weight,
    bool IsRequired,
    bool IsActive);
public sealed record RecommendationRuleInput(
    int? Id,
    int UsageNeedId,
    int CategoryId,
    int SpecificationId,
    string Operator,
    string ThresholdValue,
    decimal Weight,
    bool IsRequired,
    bool IsActive);
public sealed record ConsultationAdminOption(int Id, string Name, string? Detail = null);
public sealed record RecommendationRuleFormOptions(
    IReadOnlyList<ConsultationAdminOption> UsageNeeds,
    IReadOnlyList<ConsultationAdminOption> Categories,
    IReadOnlyList<ConsultationAdminOption> Specifications);

public sealed class ConsultationValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public interface IConsultationManagementService
{
    Task<IReadOnlyList<UsageNeedAdminRow>> GetUsageNeedsAsync(CancellationToken cancellationToken = default);
    Task<int> SaveUsageNeedAsync(UsageNeedInput input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecommendationRuleAdminRow>> GetRulesAsync(CancellationToken cancellationToken = default);
    Task<RecommendationRuleFormOptions> GetRuleFormOptionsAsync(CancellationToken cancellationToken = default);
    Task<int> SaveRuleAsync(RecommendationRuleInput input, CancellationToken cancellationToken = default);
    Task<bool> SetUsageNeedActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default);
    Task<bool> SetRecommendationRuleActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default);
}
