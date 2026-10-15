using System.Globalization;

namespace NEXORA.Domain.Entities;

public static class RecommendationRuleOperators
{
    public const string Equal = "EQ";
    public const string NotEqual = "NE";
    public const string GreaterThan = "GT";
    public const string GreaterThanOrEqual = "GTE";
    public const string LessThan = "LT";
    public const string LessThanOrEqual = "LTE";
    public const string Contains = "CONTAINS";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Equal, NotEqual, GreaterThan, GreaterThanOrEqual,
        LessThan, LessThanOrEqual, Contains
    };
}

public static class SpecificationDataTypes
{
    public const string Number = "NUMBER";
    public const string Text = "TEXT";
    public const string Boolean = "BOOLEAN";
    public const string Enumeration = "ENUM";
}

/// <summary>
/// Validates a recommendation rule against its dynamic specification type.
/// Category membership is additionally protected by a composite database FK.
/// </summary>
public static class RecommendationRulePolicy
{
    private const decimal MaximumPersistableWeight = 99_999.9999m;

    public static string? Validate(RecommendationRule rule, Specification specification)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(specification);

        if (!RecommendationRuleOperators.All.Contains(rule.Operator))
        {
            return "Unsupported recommendation operator.";
        }

        if (rule.Weight < 0 || rule.Weight > MaximumPersistableWeight)
        {
            return "Weight must be non-negative and fit decimal(9,4).";
        }

        var dataType = specification.DataType.Trim().ToUpperInvariant();
        var operation = rule.Operator;
        var threshold = rule.ThresholdValue.Trim();
        if (dataType == SpecificationDataTypes.Number)
        {
            return operation == RecommendationRuleOperators.Contains
                ? "CONTAINS is not supported for NUMBER specifications."
                : decimal.TryParse(threshold,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out _)
                    ? null
                    : "The threshold must be a decimal number using invariant formatting.";
        }

        if (dataType == SpecificationDataTypes.Text)
        {
            return operation is RecommendationRuleOperators.Equal
                or RecommendationRuleOperators.NotEqual
                or RecommendationRuleOperators.Contains
                ? string.IsNullOrWhiteSpace(threshold) ? "The threshold cannot be empty." : null
                : "Only EQ, NE, and CONTAINS are supported for TEXT specifications.";
        }

        if (dataType == SpecificationDataTypes.Enumeration)
        {
            return operation is RecommendationRuleOperators.Equal or RecommendationRuleOperators.NotEqual
                ? string.IsNullOrWhiteSpace(threshold) ? "The threshold cannot be empty." : null
                : "Only EQ and NE are supported for ENUM specifications.";
        }

        if (dataType == SpecificationDataTypes.Boolean)
        {
            return operation is RecommendationRuleOperators.Equal or RecommendationRuleOperators.NotEqual
                ? bool.TryParse(threshold, out _)
                    ? null
                    : "The threshold for BOOLEAN must be true or false."
                : "Only EQ and NE are supported for BOOLEAN specifications.";
        }

        return "The specification data type has no registered recommendation handler.";
    }
}

public sealed class UsageNeed
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<RecommendationRule> Rules { get; set; } = new List<RecommendationRule>();
}

public sealed class RecommendationRule
{
    public int Id { get; set; }
    public int UsageNeedId { get; set; }
    public int CategoryId { get; set; }
    public int SpecificationId { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string ThresholdValue { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public UsageNeed UsageNeed { get; set; } = null!;
    public Specification Specification { get; set; } = null!;
}
