using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NEXORA.Application.Consultation;

/// <summary>Bounded, deterministic Vietnamese parser. It never calls external AI services.</summary>
public sealed class VietnameseRequirementParser : IRequirementParser
{
    private const int MaximumInputLength = 1200;
    private const decimal MaximumBudgetVnd = 1_000_000_000_000m;

    private static readonly (string Canonical, string[] Aliases)[] Categories =
    [
        ("Laptop", ["laptop", "may tinh xach tay", "notebook"]),
        ("Điện thoại", ["dien thoai", "smartphone", "phone"]),
        ("Máy tính bảng", ["may tinh bang", "tablet", "ipad"])
    ];

    private static readonly (string Code, string Name, string[] Aliases)[] UsageNeeds =
    [
        ("OFFICE", "Văn phòng", ["van phong", "word", "excel", "lam viec van phong"]),
        ("STUDY", "Học tập", ["hoc tap", "hoc online", "hoc truc tuyen", "hoc sinh", "sinh vien"]),
        ("PROGRAMMING", "Lập trình", ["lap trinh", "programming", "coding", "code", "developer"]),
        ("GAMING", "Chơi game", ["choi game", "gaming", "game"]),
        ("GRAPHICS", "Đồ họa", ["do hoa", "thiet ke do hoa", "graphic design"]),
        ("PHOTO_EDITING", "Chỉnh sửa ảnh", ["chinh sua anh", "edit anh", "photo editing"]),
        ("VIDEO_EDITING", "Chỉnh sửa video", ["chinh sua video", "edit video", "dung video", "video editing"]),
        ("GENERAL", "Nhu cầu thông thường", ["nhu cau thong thuong", "co ban", "giai tri", "su dung hang ngay"])
    ];

    private static readonly (string Name, string Pattern, string CanonicalUnit)[] NumericSpecifications =
    [
        ("RAM", @"\bram\b", "GB"),
        ("SSD", @"\bssd\b", "GB"),
        ("Bộ nhớ lưu trữ", @"\b(?:bo nho luu tru|dung luong luu tru)\b", "GB"),
        ("Dung lượng pin", @"\b(?:dung luong pin|pin)\b", "mAh"),
        ("Trọng lượng", @"\b(?:trong luong|can nang)\b", "kg"),
        ("Kích thước màn hình", @"\b(?:kich thuoc man hinh|man hinh)\b", "inch")
    ];

    private static readonly Regex AmountRegex = new(
        @"(?<![a-z0-9])(?<amount>\d[\d.,]*)(?:\s*(?<scale>trieu|tr|m|ty|ti|vnd|dong|d))?(?![a-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    public ConsultationParseResult Parse(string? input)
    {
        var original = input ?? string.Empty;
        var normalized = NormalizeForMatching(original);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Failure(original, "Vui lòng nhập nhu cầu bằng tiếng Việt.");
        }

        if (original.Length > MaximumInputLength)
        {
            return Failure(original, $"Nội dung không được vượt quá {MaximumInputLength} ký tự.");
        }

        var warnings = new List<string>();
        var category = DetectSingle(original, normalized, Categories.Select(item => (item.Canonical, item.Aliases)).ToArray(), warnings, "danh mục");
        var usage = DetectSingle(original, normalized, UsageNeeds.Select(item => (item.Code, item.Aliases)).ToArray(), warnings, "mục đích sử dụng");
        if (ContainsAny(normalized, "cong viec", "cong viec", "di lam") && usage.State == RecognitionState.Missing)
        {
            usage = new RecognizedField(null, FindPhrase(original, normalized, ["cong viec", "di lam"]), RecognitionState.Uncertain);
            warnings.Add("Mô tả công việc chưa phân biệt nhu cầu văn phòng, lập trình hay thiết kế.");
        }

        var budget = ParseBudget(original, normalized, warnings);
        var hardConstraints = ParseHardConstraints(original, normalized, warnings);
        AddContradictionWarnings(hardConstraints, warnings);
        var softPreferences = ParseSoftPreferences(original, normalized);
        var missing = new List<string>();
        var questions = new List<string>();

        if (category.State is RecognitionState.Missing or RecognitionState.Uncertain)
        {
            missing.Add("danh mục");
            questions.Add("Bạn muốn tìm laptop, điện thoại hay máy tính bảng?");
        }

        if (usage.State is RecognitionState.Missing or RecognitionState.Uncertain)
        {
            missing.Add("mục đích sử dụng");
            questions.Add("Bạn dùng thiết bị chủ yếu cho văn phòng, học tập, lập trình, chơi game, đồ họa hay nhu cầu khác?");
        }

        if (budget.State == RecognitionState.Uncertain)
        {
            questions.Add("Bạn có thể xác nhận mức ngân sách hoặc cho biết đây chỉ là mức tham khảo không?");
        }

        foreach (var constraint in hardConstraints.Where(value => value.State == RecognitionState.Uncertain))
        {
            questions.Add($"Bạn muốn {constraint.SpecificationName} {constraint.Operator} {constraint.Value.ToString(CultureInfo.InvariantCulture)} {constraint.Unit}, đúng không?");
        }

        foreach (var warning in warnings.Where(value => value.Contains("mâu thuẫn", StringComparison.OrdinalIgnoreCase)))
        {
            questions.Add("Bạn vui lòng xác nhận lại điều kiện đang mâu thuẫn.");
        }

        var profile = new RequirementProfile(
            original.Trim(), category, budget, usage, hardConstraints, softPreferences,
            missing.Distinct(StringComparer.Ordinal).ToArray(), questions.Distinct(StringComparer.Ordinal).ToArray(), warnings);
        return new ConsultationParseResult(true, profile, null);
    }

    private static ConsultationParseResult Failure(string input, string error)
    {
        var profile = new RequirementProfile(
            input, new RecognizedField(null, null, RecognitionState.Missing),
            new BudgetRequirement(BudgetKind.None, null, null, null, null, RecognitionState.Missing),
            new RecognizedField(null, null, RecognitionState.Missing), [], [],
            ["danh mục", "mục đích sử dụng"], [], []);
        return new ConsultationParseResult(false, profile, error);
    }

    private static RecognizedField DetectSingle(
        string original,
        string normalized,
        IReadOnlyList<(string Canonical, string[] Aliases)> definitions,
        ICollection<string> warnings,
        string fieldName)
    {
        var matches = definitions
            .SelectMany(definition => definition.Aliases
                .Where(alias => ContainsPhrase(normalized, alias))
                .Select(alias => (definition.Canonical, Alias: alias)))
            .GroupBy(match => match.Canonical, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        if (matches.Length == 0)
        {
            return new RecognizedField(null, null, RecognitionState.Missing);
        }

        if (matches.Length > 1)
        {
            warnings.Add($"Câu nhập có nhiều giá trị {fieldName} khác nhau.");
            return new RecognizedField(null, FindPhrase(original, normalized, matches.Select(match => match.Alias).ToArray()), RecognitionState.Uncertain);
        }

        var match = matches[0];
        var state = string.Equals(NormalizeForMatching(match.Canonical), match.Alias, StringComparison.Ordinal)
            ? RecognitionState.Explicit
            : RecognitionState.Normalized;
        return new RecognizedField(match.Canonical, FindPhrase(original, normalized, [match.Alias]), state);
    }

    private static BudgetRequirement ParseBudget(string original, string normalized, ICollection<string> warnings)
    {
        var candidates = new List<(Match Match, decimal Amount, string Raw)>();
        foreach (Match match in AmountRegex.Matches(normalized))
        {
            var after = normalized[(match.Index + match.Length)..];
            if (Regex.IsMatch(after, @"^\s*(?:gb|mb|tb|kg|g|mah|ah|inch|inches)\b", RegexOptions.CultureInvariant))
            {
                continue;
            }

            var beforeStart = Math.Max(0, match.Index - 36);
            var before = normalized[beforeStart..match.Index];
            var scale = match.Groups["scale"].Value;
            var hasBudgetCue = ContainsAny(before, "ngan sach", "budget", "duoi", "khong qua", "toi da", "tam", "khoang", "tu", "gia") ||
                               ContainsAny(after[..Math.Min(after.Length, 12)], "trieu", "tr", "ty", "dong");
            if (string.IsNullOrEmpty(scale) && !hasBudgetCue)
            {
                continue;
            }

            var raw = match.Groups["amount"].Value;
            var multiplier = scale switch
            {
                "trieu" or "tr" or "m" => 1_000_000m,
                "ty" or "ti" => 1_000_000_000m,
                _ => 1m
            };
            if (!TryParseLocalizedNumber(raw, out var amount))
            {
                warnings.Add("Không chuẩn hóa được số tiền trong ngân sách.");
                continue;
            }

            amount *= multiplier;
            if (amount is <= 0 or > MaximumBudgetVnd)
            {
                warnings.Add("Ngân sách phải lớn hơn 0 và không vượt 1.000 tỷ đồng.");
                return new BudgetRequirement(BudgetKind.None, null, null, null, match.Value, RecognitionState.Uncertain);
            }

            candidates.Add((match, amount, match.Value));
        }

        if (candidates.Count == 0)
        {
            var hasBudgetCue = ContainsAny(normalized, "ngan sach", "budget", "duoi", "khong qua", "toi da", "tam", "khoang") || normalized.Contains('~');
            return hasBudgetCue
                ? new BudgetRequirement(BudgetKind.None, null, null, null, null, RecognitionState.Uncertain)
                : new BudgetRequirement(BudgetKind.None, null, null, null, null, RecognitionState.Missing);
        }

        var first = candidates[0];
        var remainder = normalized[(first.Match.Index + first.Match.Length)..];
        var nextAmount = candidates.Skip(1).FirstOrDefault(candidate => candidate.Match.Index > first.Match.Index);
        if (nextAmount.Match is not null && Regex.IsMatch(remainder[..Math.Min(remainder.Length, nextAmount.Match.Index - first.Match.Index - first.Match.Length)], @"\b(?:den|toi)\b"))
        {
            var firstScaleFactor = GetMoneyScale(first.Match.Groups["scale"].Value);
            var nextScaleFactor = GetMoneyScale(nextAmount.Match.Groups["scale"].Value);
            var lowerBound = firstScaleFactor == 1m && nextScaleFactor > 1m
                ? first.Amount * nextScaleFactor
                : first.Amount;
            var upperBound = nextScaleFactor == 1m && firstScaleFactor > 1m
                ? nextAmount.Amount * firstScaleFactor
                : nextAmount.Amount;
            if (upperBound < lowerBound)
            {
                warnings.Add("Khoảng ngân sách mâu thuẫn: cận dưới lớn hơn cận trên.");
                return new BudgetRequirement(BudgetKind.Range, lowerBound, upperBound, null,
                    first.Raw + " … " + nextAmount.Raw, RecognitionState.Uncertain);
            }

            return new BudgetRequirement(BudgetKind.Range, lowerBound, upperBound, null,
                first.Raw + " … " + nextAmount.Raw, RecognitionState.Normalized);
        }

        var beforeFirst = normalized[Math.Max(0, first.Match.Index - 36)..first.Match.Index];
        var isMaximum = ContainsAny(beforeFirst, "duoi", "khong qua", "toi da", "<=", "max");
        if (isMaximum)
        {
            return new BudgetRequirement(BudgetKind.Maximum, null, first.Amount, null, first.Raw, RecognitionState.Normalized);
        }

        var isApproximate = ContainsAny(beforeFirst, "tam", "khoang", "~") ||
                            ContainsAny(remainder[..Math.Min(remainder.Length, 8)], "tam", "khoang");
        if (!isApproximate && ContainsAny(beforeFirst, "ngan sach", "budget"))
        {
            isMaximum = true;
        }

        return isMaximum
            ? new BudgetRequirement(BudgetKind.Maximum, null, first.Amount, null, first.Raw, RecognitionState.Normalized)
            : new BudgetRequirement(BudgetKind.Approximate, null, null, first.Amount, first.Raw,
                RecognitionState.Normalized);
    }

    private static IReadOnlyList<HardSpecificationConstraint> ParseHardConstraints(
        string original,
        string normalized,
        ICollection<string> warnings)
    {
        var result = new List<HardSpecificationConstraint>();
        foreach (var specification in NumericSpecifications)
        {
            var pattern = $@"(?<prefix>(?:it nhat|toi thieu|lon hon hoac bang|tu|khong qua|toi da|nho hon hoac bang|duoi|lon hon|nho hon|>=|<=|>|<)?\s*)(?<value>\d+(?:[.,]\d+)?)(?:\s*(?<unit>tb|gb|mb|mah|ah|kg|g|inch|inches|cm|mm))?\s*{specification.Pattern}|{specification.Pattern}\s*(?<prefix2>(?:it nhat|toi thieu|lon hon hoac bang|tu|khong qua|toi da|nho hon hoac bang|duoi|lon hon|nho hon|>=|<=|>|<)?\s*)(?<value2>\d+(?:[.,]\d+)?)(?:\s*(?<unit2>tb|gb|mb|mah|ah|kg|g|inch|inches|cm|mm))?";
            foreach (Match match in Regex.Matches(normalized, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
            {
                var valueText = match.Groups["value"].Success ? match.Groups["value"].Value : match.Groups["value2"].Value;
                var unitText = match.Groups["unit"].Success ? match.Groups["unit"].Value : match.Groups["unit2"].Value;
                var prefix = match.Groups["prefix"].Success ? match.Groups["prefix"].Value : match.Groups["prefix2"].Value;
                if (!TryParseLocalizedNumber(valueText, out var value))
                {
                    warnings.Add($"Không chuẩn hóa được ngưỡng {specification.Name}.");
                    continue;
                }

                var unitResult = NormalizeUnit(value, unitText, specification.CanonicalUnit);
                var recognized = unitResult.IsCompatible && !string.IsNullOrWhiteSpace(unitText);
                if (!unitResult.IsCompatible)
                {
                    warnings.Add($"Đơn vị {unitText} không tương thích với {specification.Name} ({specification.CanonicalUnit}).");
                }
                else if (string.IsNullOrWhiteSpace(unitText))
                {
                    warnings.Add($"Thiếu đơn vị cho ngưỡng {specification.Name}.");
                }

                var operatorValue = ParseOperator(prefix);
                var state = recognized && operatorValue.Explicit
                    ? RecognitionState.Normalized
                    : RecognitionState.Uncertain;
                var snippetStart = Math.Max(0, match.Index - 15);
                var snippetLength = Math.Min(normalized.Length - snippetStart, match.Length + match.Index - snippetStart + 12);
                var snippet = normalized.Substring(snippetStart, snippetLength).Trim();
                if (!operatorValue.Explicit)
                {
                    warnings.Add($"Ngưỡng {specification.Name} chưa có toán tử rõ ràng.");
                }

                result.Add(new HardSpecificationConstraint(
                    specification.Name, operatorValue.Operator, unitResult.Value, specification.CanonicalUnit,
                    snippet, state));
            }
        }

        return result;
    }

    private static void AddContradictionWarnings(
        IReadOnlyList<HardSpecificationConstraint> constraints,
        ICollection<string> warnings)
    {
        foreach (var group in constraints.GroupBy(item => ConsultationNameNormalizer.Normalize(item.SpecificationName)))
        {
            var lower = group.Where(item => item.Operator is "GT" or "GTE").ToArray();
            var upper = group.Where(item => item.Operator is "LT" or "LTE").ToArray();
            var equals = group.Where(item => item.Operator == "EQ").ToArray();
            if (lower.Length > 0 && upper.Length > 0)
            {
                var lowerBound = lower.Max(item => item.Value);
                var upperBound = upper.Min(item => item.Value);
                var strictAtEqual = lower.Any(item => item.Value == lowerBound && item.Operator == "GT") ||
                                    upper.Any(item => item.Value == upperBound && item.Operator == "LT");
                if (lowerBound > upperBound || (lowerBound == upperBound && strictAtEqual))
                {
                    warnings.Add($"Các ngưỡng {group.First().SpecificationName} mâu thuẫn nhau.");
                }
            }

            if (equals.Select(item => item.Value).Distinct().Count() > 1 ||
                (equals.Length > 0 && lower.Any(item => item.Value > equals[0].Value ||
                    (item.Value == equals[0].Value && item.Operator == "GT"))) ||
                (equals.Length > 0 && upper.Any(item => item.Value < equals[0].Value ||
                    (item.Value == equals[0].Value && item.Operator == "LT")))
            )
            {
                warnings.Add($"Các ngưỡng {group.First().SpecificationName} mâu thuẫn với nhau.");
            }
        }
    }

    private static IReadOnlyList<SoftSpecificationPreference> ParseSoftPreferences(string original, string normalized)
    {
        var result = new List<SoftSpecificationPreference>();
        AddIfFound(result, original, normalized, "RAM", PreferenceDirection.Higher,
            ["ram lon", "ram cao", "uu tien ram"]);
        AddIfFound(result, original, normalized, "SSD", PreferenceDirection.Higher,
            ["ssd lon", "uu tien ssd"]);
        AddIfFound(result, original, normalized, "Dung lượng pin", PreferenceDirection.Higher,
            ["pin tot", "pin lau", "pin trau", "uu tien pin", "thoi luong pin"]);
        AddIfFound(result, original, normalized, "Trọng lượng", PreferenceDirection.Lower,
            ["may nhe", "nhe", "trong luong thap", "uu tien nhe"]);
        AddIfFound(result, original, normalized, "CPU", PreferenceDirection.Higher,
            ["cpu manh", "xu ly manh", "uu tien cpu"]);
        AddIfFound(result, original, normalized, "GPU", PreferenceDirection.Categorical,
            ["gpu roi", "card roi", "do hoa manh"]);
        AddIfFound(result, original, normalized, "Kích thước màn hình", PreferenceDirection.Higher,
            ["man hinh lon", "uu tien man hinh"]);
        AddIfFound(result, original, normalized, "Bộ nhớ lưu trữ", PreferenceDirection.Higher,
            ["bo nho lon", "dung luong lon", "uu tien dung luong"]);
        return result
            .GroupBy(item => item.SpecificationName, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static void AddIfFound(
        ICollection<SoftSpecificationPreference> target,
        string original,
        string normalized,
        string specificationName,
        PreferenceDirection direction,
        IReadOnlyList<string> phrases)
    {
        var phrase = phrases.FirstOrDefault(value => ContainsPhrase(normalized, value));
        if (phrase is not null)
        {
            target.Add(new SoftSpecificationPreference(specificationName, direction,
                FindPhrase(original, normalized, [phrase]) ?? phrase, RecognitionState.Normalized));
        }
    }

    private static (string Operator, bool Explicit) ParseOperator(string prefix)
    {
        if (ContainsAny(prefix, "it nhat", "toi thieu", "lon hon hoac bang", "tu", ">=")) return ("GTE", true);
        if (ContainsAny(prefix, "khong qua", "toi da", "nho hon hoac bang", "<=")) return ("LTE", true);
        if (ContainsAny(prefix, "lon hon", ">")) return ("GT", true);
        if (ContainsAny(prefix, "nho hon", "duoi", "<")) return ("LT", true);
        return ("GTE", false);
    }

    private static (decimal Value, bool IsCompatible) NormalizeUnit(decimal value, string unit, string expected)
    {
        if (string.IsNullOrWhiteSpace(unit)) return (value, true);
        var normalized = unit.ToLowerInvariant();
        if (expected == "GB")
        {
            return normalized switch
            {
                "gb" => (value, true),
                "tb" => (value * 1024m, true),
                "mb" => (value / 1024m, true),
                _ => (value, false)
            };
        }

        if (expected == "kg")
        {
            return normalized switch { "kg" => (value, true), "g" => (value / 1000m, true), _ => (value, false) };
        }

        if (expected == "mAh")
        {
            return normalized switch { "mah" => (value, true), "ah" => (value * 1000m, true), _ => (value, false) };
        }

        if (expected == "inch")
        {
            return normalized switch
            {
                "inch" or "inches" => (value, true),
                "cm" => (value / 2.54m, true),
                "mm" => (value / 25.4m, true),
                _ => (value, false)
            };
        }

        return (value, false);
    }

    private static bool TryParseLocalizedNumber(string value, out decimal number)
    {
        var text = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (text.Contains('.') && text.Contains(','))
        {
            text = text.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }
        else if (text.Contains('.') || text.Contains(','))
        {
            var separator = text.Contains('.') ? '.' : ',';
            var groups = text.Split(separator);
            if (groups.Length > 2 || (groups.Length == 2 && groups[1].Length == 3))
            {
                text = text.Replace(separator.ToString(), string.Empty, StringComparison.Ordinal);
            }
            else
            {
                text = text.Replace(separator, '.');
            }
        }

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
    }

    private static decimal GetMoneyScale(string scale) => scale switch
    {
        "trieu" or "tr" or "m" => 1_000_000m,
        "ty" or "ti" => 1_000_000_000m,
        _ => 1m
    };

    private static string NormalizeForMatching(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character));
        }

        var withoutMarks = builder.ToString().Normalize(NormalizationForm.FormC);
        return Regex.Replace(withoutMarks, @"(?<![<>=])[^a-z0-9.,~]+(?![<>=])", " ", RegexOptions.CultureInvariant)
            .Replace("> =", ">=", StringComparison.Ordinal)
            .Replace("< =", "<=", StringComparison.Ordinal)
            .Trim();
    }

    private static bool ContainsPhrase(string text, string phrase) =>
        Regex.IsMatch(text, $@"(?<![a-z0-9]){Regex.Escape(phrase)}(?![a-z0-9])", RegexOptions.CultureInvariant);

    private static bool ContainsAny(string text, params string[] phrases) => phrases.Any(phrase => text.Contains(phrase, StringComparison.Ordinal));

    private static string? FindPhrase(string original, string normalized, IReadOnlyList<string> phrases)
    {
        return phrases.Any(phrase => ContainsPhrase(normalized, phrase)) ? original.Trim() : null;
    }
}
