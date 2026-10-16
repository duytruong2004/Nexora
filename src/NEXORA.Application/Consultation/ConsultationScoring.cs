using System.Globalization;
using System.Text;
using NEXORA.Domain.Entities;

namespace NEXORA.Application.Consultation;

public static class ConsultationNameNormalizer
{
    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    public static bool SpecificationMatches(string databaseName, string requestedName)
    {
        var actual = Normalize(databaseName);
        var requested = Normalize(requestedName);
        if (actual == requested) return true;
        return requested switch
        {
            "ram" => actual == "ram",
            "ssd" => actual == "ssd",
            "bo nho luu tru" => actual == "bo nho luu tru" || actual == "dung luong luu tru",
            "dung luong pin" => actual == "dung luong pin",
            "trong luong" => actual == "trong luong" || actual == "can nang",
            "kich thuoc man hinh" => actual == "kich thuoc man hinh" || actual == "man hinh",
            _ => false
        };
    }

    public static string NormalizeUnit(string? unit) => unit?.Trim().ToLowerInvariant() ?? string.Empty;
}

/// <summary>Deterministic weighted scoring and hard-rule evaluation over database-backed values.</summary>
public static class RecommendationScoringEngine
{
    public static IReadOnlyList<RecommendedProduct> Rank(
        ConsultationDataContext context,
        IReadOnlyList<ConsultationProduct> products,
        RequirementProfile profile)
    {
        var activeRules = context.Rules;
        var requiredRules = activeRules.Where(rule => rule.IsRequired).ToArray();
        var requestedPreferenceNames = profile.SoftPreferences.Select(item => item.SpecificationName).ToArray();
        var softRules = activeRules.Where(rule => rule.Weight > 0 &&
            (requestedPreferenceNames.Length == 0 || requestedPreferenceNames.Any(name =>
                ConsultationNameNormalizer.SpecificationMatches(rule.SpecificationName, name)))).ToArray();

        var results = new List<RecommendedProduct>(products.Count);
        foreach (var product in products)
        {
            var valuesBySpecification = product.Specifications.ToDictionary(value => value.SpecificationId);
            var requiredPassed = true;
            foreach (var rule in requiredRules)
            {
                if (!valuesBySpecification.TryGetValue(rule.SpecificationId, out var value) ||
                    !EvaluatePredicate(value, rule.Operator, rule.ThresholdValue))
                {
                    requiredPassed = false;
                    break;
                }
            }

            if (!requiredPassed) continue;

            var numerator = 0m;
            var denominator = 0m;
            var criteria = new List<RecommendationCriterionResult>();
            var reasons = new List<string>();
            var limitations = new List<string>();
            foreach (var rule in softRules)
            {
                if (!valuesBySpecification.TryGetValue(rule.SpecificationId, out var value))
                {
                    denominator += rule.Weight;
                    criteria.Add(new RecommendationCriterionResult(
                        rule.SpecificationName, rule.Weight, null, false, null,
                        rule.Operator, rule.ThresholdValue, null));
                    limitations.Add($"Chưa có dữ liệu {rule.SpecificationName} để chấm tiêu chí này.");
                    continue;
                }

                var score = Score(value, rule.Operator, rule.ThresholdValue);
                criteria.Add(new RecommendationCriterionResult(
                    rule.SpecificationName, rule.Weight, score, rule.IsRequired, FormatValue(value),
                    rule.Operator, rule.ThresholdValue, score >= 0.5m ? "Đáp ứng tiêu chí ưu tiên đã cấu hình." : null));
                numerator += rule.Weight * score;
                denominator += rule.Weight;
                if (score >= 0.5m)
                {
                    reasons.Add($"{rule.SpecificationName}: {FormatValue(value)}; tiêu chí ưu tiên đang cấu hình là {FormatCondition(rule)}.");
                }
                else
                {
                    limitations.Add($"{rule.SpecificationName}: dữ liệu hiện có là {FormatValue(value)}; tiêu chí ưu tiên đang cấu hình là {FormatCondition(rule)} và chưa đạt tốt.");
                }
            }

            foreach (var rule in requiredRules.Where(rule => rule.Weight == 0))
            {
                var value = valuesBySpecification[rule.SpecificationId];
                criteria.Add(new RecommendationCriterionResult(
                    rule.SpecificationName, rule.Weight, 1m, true, FormatValue(value),
                    rule.Operator, rule.ThresholdValue, "Đã đạt điều kiện bắt buộc trong cấu hình tư vấn."));
                reasons.Add($"Đạt điều kiện bắt buộc {rule.SpecificationName}: {FormatValue(value)}.");
            }

            foreach (var rule in requiredRules.Where(rule => rule.Weight > 0))
            {
                reasons.Add($"Đạt điều kiện bắt buộc {rule.SpecificationName}: {FormatValue(valuesBySpecification[rule.SpecificationId])}.");
            }

            foreach (var preference in profile.SoftPreferences)
            {
                if (!activeRules.Any(rule => rule.Weight > 0 &&
                    ConsultationNameNormalizer.SpecificationMatches(rule.SpecificationName, preference.SpecificationName)))
                {
                    if (!context.Specifications.Any(specification =>
                            ConsultationNameNormalizer.SpecificationMatches(specification.Name, preference.SpecificationName)))
                    {
                        limitations.Add($"Danh mục {context.CategoryName} chưa khai báo thông số {preference.SpecificationName}; không thể đánh giá ưu tiên này.");
                    }
                    else
                    {
                        limitations.Add($"Chưa có quy tắc trọng số cho ưu tiên {preference.SpecificationName}.");
                    }
                }
            }

            var scoreValue = denominator == 0m ? 0m : decimal.Round(numerator / denominator, 6, MidpointRounding.AwayFromZero);
            var effectivePrice = product.DiscountPrice ?? product.Price;
            if ((profile.Budget.Kind is BudgetKind.Maximum or BudgetKind.Range) &&
                profile.Budget.MaximumVnd.HasValue)
            {
                reasons.Add($"Giá hiện tại {effectivePrice.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} ₫ nằm trong ngân sách đã nêu.");
            }

            if (reasons.Count == 0)
            {
                reasons.Add($"Sản phẩm thuộc danh mục {product.CategoryName}; điểm được tính từ các quy tắc tư vấn đang hoạt động.");
            }

            results.Add(new RecommendedProduct(product, scoreValue, reasons.Distinct().ToArray(),
                limitations.Distinct().ToArray(), criteria));
        }

        return results
            .OrderByDescending(item => item.Score)
            .ThenBy(item => profile.Budget.Kind == BudgetKind.Approximate && profile.Budget.TargetVnd.HasValue
                ? Math.Abs((item.Product.DiscountPrice ?? item.Product.Price) - profile.Budget.TargetVnd.Value)
                : (item.Product.DiscountPrice ?? item.Product.Price))
            .ThenBy(item => item.Product.Id)
            .ToArray();
    }

    public static decimal Score(ConsultationSpecificationValue value, string operation, string threshold)
    {
        if (value.NumericValue is decimal numeric &&
            decimal.TryParse(threshold, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var numericThreshold))
        {
            return operation switch
            {
                RecommendationRuleOperators.GreaterThanOrEqual or RecommendationRuleOperators.GreaterThan =>
                    numericThreshold <= 0m ? 1m : Math.Clamp(numeric / numericThreshold, 0m, 1m),
                RecommendationRuleOperators.LessThanOrEqual or RecommendationRuleOperators.LessThan =>
                    numeric <= numericThreshold ? 1m : numeric <= 0m || numericThreshold <= 0m ? 0m : Math.Clamp(numericThreshold / numeric, 0m, 1m),
                RecommendationRuleOperators.Equal => numeric == numericThreshold ? 1m : 0m,
                RecommendationRuleOperators.NotEqual => numeric != numericThreshold ? 1m : 0m,
                _ => 0m
            };
        }

        var valueText = value.Value.Trim();
        var thresholdText = threshold.Trim();
        return operation switch
        {
            RecommendationRuleOperators.Equal => string.Equals(valueText, thresholdText, StringComparison.OrdinalIgnoreCase) ? 1m : 0m,
            RecommendationRuleOperators.NotEqual => string.Equals(valueText, thresholdText, StringComparison.OrdinalIgnoreCase) ? 0m : 1m,
            RecommendationRuleOperators.Contains => valueText.Contains(thresholdText, StringComparison.OrdinalIgnoreCase) ? 1m : 0m,
            _ => 0m
        };
    }

    public static bool EvaluatePredicate(ConsultationSpecificationValue value, string operation, string threshold)
    {
        if (value.NumericValue is decimal numeric &&
            decimal.TryParse(threshold, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var numericThreshold))
        {
            return operation switch
            {
                RecommendationRuleOperators.Equal => numeric == numericThreshold,
                RecommendationRuleOperators.NotEqual => numeric != numericThreshold,
                RecommendationRuleOperators.GreaterThan => numeric > numericThreshold,
                RecommendationRuleOperators.GreaterThanOrEqual => numeric >= numericThreshold,
                RecommendationRuleOperators.LessThan => numeric < numericThreshold,
                RecommendationRuleOperators.LessThanOrEqual => numeric <= numericThreshold,
                _ => false
            };
        }

        return operation switch
        {
            RecommendationRuleOperators.Equal => string.Equals(value.Value.Trim(), threshold.Trim(), StringComparison.OrdinalIgnoreCase),
            RecommendationRuleOperators.NotEqual => !string.Equals(value.Value.Trim(), threshold.Trim(), StringComparison.OrdinalIgnoreCase),
            RecommendationRuleOperators.Contains => value.Value.Contains(threshold, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string FormatValue(ConsultationSpecificationValue value)
    {
        if (!string.IsNullOrWhiteSpace(value.DisplayValue)) return value.DisplayValue;
        return string.IsNullOrWhiteSpace(value.Unit) ? value.Value : $"{value.Value} {value.Unit}";
    }

    private static string FormatCondition(ConsultationRuleData rule)
    {
        var operation = rule.Operator switch
        {
            RecommendationRuleOperators.GreaterThanOrEqual => "≥",
            RecommendationRuleOperators.GreaterThan => ">",
            RecommendationRuleOperators.LessThanOrEqual => "≤",
            RecommendationRuleOperators.LessThan => "<",
            RecommendationRuleOperators.Equal => "=",
            RecommendationRuleOperators.NotEqual => "≠",
            RecommendationRuleOperators.Contains => "chứa",
            _ => rule.Operator
        };
        var threshold = string.IsNullOrWhiteSpace(rule.Unit) ? rule.ThresholdValue : $"{rule.ThresholdValue} {rule.Unit}";
        return $"{operation} {threshold}";
    }
}
