using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NEXORA.Application.Consultation;

public sealed class ConsultationOrchestrator(
    IRequirementParser parser,
    IConsultationAiAssistant aiAssistant,
    IConsultationDataSource dataSource) : IConsultationService
{
    private static readonly IReadOnlyDictionary<string, string> CategoryNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Laptop"] = "Laptop",
        ["Điện thoại"] = "Điện thoại",
        ["Máy tính bảng"] = "Máy tính bảng",
        ["Màn hình"] = "Màn hình",
        ["Bàn phím"] = "Bàn phím",
        ["Chuột"] = "Chuột",
        ["Tai nghe"] = "Tai nghe",
        ["Đồng hồ thông minh"] = "Đồng hồ thông minh",
        ["Máy ảnh"] = "Máy ảnh",
        ["Phụ kiện công nghệ"] = "Phụ kiện công nghệ"
    };

    private static readonly (string BrandName, string[] Aliases)[] BrandAliases =
    [
        ("Apple", ["iphone", "i phone", "ipad", "macbook"])
    ];

    public async Task<ConsultationOutcome> ConsultAsync(
        ConsultationRequest request,
        CancellationToken cancellationToken = default)
    {
        var parsed = parser.Parse(request.Description);
        if (!parsed.IsValid)
        {
            return new ConsultationOutcome(ConsultationOutcomeKind.InvalidInput, parsed.Profile, [], parsed.Error!);
        }

        var profile = parsed.Profile;
        var aiAssisted = false;
        // Qwen receives the bounded conversational context on every turn. The local parser
        // remains authoritative for explicit values and still supplies a complete fallback.
        if (aiAssistant.IsAvailable)
        {
            var allowedUsageNeeds = await dataSource.GetActiveUsageNeedsAsync(cancellationToken);
            var interpretation = await aiAssistant.InterpretAsync(profile.Input, allowedUsageNeeds, cancellationToken);
            if (interpretation is not null)
            {
                profile = ApplyAiInterpretation(profile, interpretation, allowedUsageNeeds);
                aiAssisted = true;
            }
        }

        profile = ApplyConfirmations(profile, request);
        var hasExplicitProductRequest = BrandAliases.Any(alias =>
            alias.Aliases.Any(value => FindLastPhrasePosition(profile.Input, value) >= 0));
        var canSearchWithoutUsage = hasExplicitProductRequest ||
            (profile.Category.Value is not null && profile.UsageNeed.State == RecognitionState.Missing);
        var deferUsageQuestionUntilCategoryIsKnown = profile.Category.Value is null;
        var questions = profile.ClarificationQuestions
            .Where(question => !((canSearchWithoutUsage || deferUsageQuestionUntilCategoryIsKnown) &&
                question.StartsWith("Bạn dùng thiết bị chủ yếu", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (profile.Category.Value is null)
        {
            questions.Add("Bạn muốn tìm laptop, điện thoại hay máy tính bảng?");
        }

        if (profile.Category.Value is not null && profile.UsageNeed.Value is null && !canSearchWithoutUsage &&
            !questions.Any(question => question.StartsWith("Bạn dùng thiết bị chủ yếu", StringComparison.OrdinalIgnoreCase)))
        {
            questions.Add("Bạn dùng thiết bị chủ yếu cho mục đích nào?");
        }

        if (profile.Budget.State == RecognitionState.Uncertain)
        {
            questions.Add("Bạn có thể xác nhận lại ngân sách hoặc cho biết đây chỉ là mức tham khảo không?");
        }

        foreach (var uncertain in profile.HardConstraints.Where(item => item.State == RecognitionState.Uncertain))
        {
            questions.Add($"Vui lòng xác nhận ngưỡng {uncertain.SpecificationName} và đơn vị {uncertain.Unit}.");
        }

        if (questions.Count > 0)
        {
            var clarificationProfile = profile with
            {
                MissingFields = profile.MissingFields
                    .Where(field => !((canSearchWithoutUsage || deferUsageQuestionUntilCategoryIsKnown) && field == "mục đích sử dụng"))
                    .Concat(profile.Category.Value is null ? ["danh mục"] : [])
                    .Concat(profile.UsageNeed.Value is null && !canSearchWithoutUsage ? ["mục đích sử dụng"] : [])
                    .Distinct(StringComparer.Ordinal).ToArray(),
                ClarificationQuestions = questions.Distinct(StringComparer.Ordinal).ToArray()
            };
            return new ConsultationOutcome(ConsultationOutcomeKind.ClarificationRequired, clarificationProfile, [],
                "Cần xác nhận một số thông tin trước khi tìm sản phẩm.", AiAssisted: aiAssisted);
        }

        if (profile.Category.Value is null || !CategoryNames.ContainsKey(profile.Category.Value) ||
            (profile.UsageNeed.Value is null && !canSearchWithoutUsage))
        {
            return new ConsultationOutcome(ConsultationOutcomeKind.ClarificationRequired, profile, [],
                "Danh mục hoặc mục đích chưa nằm trong phạm vi tư vấn hiện tại.", AiAssisted: aiAssisted);
        }

        var timer = Stopwatch.StartNew();
        var useNeutralProductRanking = profile.UsageNeed.Value is null && canSearchWithoutUsage;
        var contextUsageNeed = profile.UsageNeed.Value;
        if (contextUsageNeed is null)
        {
            contextUsageNeed = (await dataSource.GetActiveUsageNeedsAsync(cancellationToken))
                .FirstOrDefault()?.Code;
        }

        if (contextUsageNeed is null)
        {
            return new ConsultationOutcome(ConsultationOutcomeKind.ClarificationRequired, profile, [],
                "Chưa có cấu hình danh mục để tìm sản phẩm. Vui lòng thử lại sau.", AiAssisted: aiAssisted);
        }

        var context = await dataSource.GetContextAsync(
            CategoryNames[profile.Category.Value], contextUsageNeed, cancellationToken);
        if (context is null)
        {
            return new ConsultationOutcome(ConsultationOutcomeKind.ClarificationRequired, profile, [],
                "Danh mục hoặc nhu cầu này chưa có cấu hình tư vấn hoạt động. Vui lòng chọn nhu cầu khác hoặc liên hệ quản trị viên.",
                AiAssisted: aiAssisted);
        }

        if (useNeutralProductRanking)
        {
            // A named model/brand is enough to search. A use-case context is loaded only
            // for category/specification metadata; its use-case rules must not filter results.
            context = context with { Rules = [] };
        }

        var numericConstraints = new List<(int SpecificationId, string Operator, decimal Value)>();
        foreach (var constraint in profile.HardConstraints)
        {
            var definition = context.Specifications.FirstOrDefault(item =>
                ConsultationNameNormalizer.SpecificationMatches(item.Name, constraint.SpecificationName));
            if (definition is null || definition.DataType != "NUMBER" ||
                !string.Equals(ConsultationNameNormalizer.NormalizeUnit(definition.Unit),
                    ConsultationNameNormalizer.NormalizeUnit(constraint.Unit), StringComparison.Ordinal))
            {
                var question = $"Danh mục {context.CategoryName} chưa có thông số {constraint.SpecificationName} với đơn vị {constraint.Unit}; hãy bỏ hoặc sửa điều kiện này.";
                var profileWithWarning = profile with
                {
                    Warnings = profile.Warnings.Append(question).ToArray(),
                    ClarificationQuestions = [question]
                };
                return new ConsultationOutcome(ConsultationOutcomeKind.ClarificationRequired, profileWithWarning, [],
                    "Không thể áp dụng điều kiện kỹ thuật với dữ liệu hiện có.", AiAssisted: aiAssisted);
            }

            numericConstraints.Add((definition.Id, constraint.Operator, constraint.Value));
        }

        var budget = profile.Budget;
        var activeBrands = await dataSource.GetActiveBrandNamesAsync(cancellationToken);
        var brandMatches = activeBrands
            .Select(name => (Name: name, Position: FindLastPhrasePosition(profile.Input, name)))
            .ToList();
        foreach (var alias in BrandAliases)
        {
            var catalogBrand = activeBrands.FirstOrDefault(name => string.Equals(
                ConsultationNameNormalizer.Normalize(name),
                ConsultationNameNormalizer.Normalize(alias.BrandName),
                StringComparison.Ordinal)) ?? alias.BrandName;
            brandMatches.AddRange(alias.Aliases.Select(value =>
                (Name: catalogBrand, Position: FindLastPhrasePosition(profile.Input, value))));
        }

        var requestedBrand = brandMatches
            .Where(item => item.Position >= 0)
            .OrderByDescending(item => item.Position)
            .ThenByDescending(item => item.Name.Length)
            .Select(item => item.Name)
            .FirstOrDefault();
        var query = new CandidateQuery(
            context.CategoryId,
            budget.Kind == BudgetKind.Range ? budget.MinimumVnd : null,
            budget.Kind is BudgetKind.Maximum or BudgetKind.Range ? budget.MaximumVnd : null,
            numericConstraints)
        {
            BrandName = requestedBrand
        };
        var candidates = await dataSource.FindCandidatesAsync(query, cancellationToken);
        var connectivityRequirement = ConsultationProductRequirementMatcher.GetConnectivityRequirement(profile.Input);
        if (connectivityRequirement is not null)
        {
            candidates = candidates
                .Where(product => ConsultationProductRequirementMatcher.Matches(product, connectivityRequirement.Value))
                .ToArray();
        }

        var ranked = useNeutralProductRanking
            ? RankNeutralMatches(candidates, requestedBrand, context.CategoryName, profile)
            : RecommendationScoringEngine.Rank(context, candidates, profile);
        timer.Stop();

        if (ranked.Count == 0)
        {
            var noMatchMessage = requestedBrand is null
                ? "Mình chưa tìm thấy sản phẩm phù hợp với các điều kiện trong danh mục NEXORA hiện có."
                : $"Mình chưa tìm thấy sản phẩm {requestedBrand} phù hợp trong danh mục NEXORA hiện có.";
            return new ConsultationOutcome(ConsultationOutcomeKind.NoMatchingProducts, profile, [],
                noMatchMessage,
                timer.ElapsedMilliseconds, aiAssisted);
        }

        var outcomeProfile = profile with
        {
            Category = new RecognizedField(context.CategoryName, profile.Category.OriginalText, profile.Category.State),
            UsageNeed = useNeutralProductRanking
                ? profile.UsageNeed
                : new RecognizedField(context.UsageNeedName, profile.UsageNeed.OriginalText, profile.UsageNeed.State)
        };
        return new ConsultationOutcome(ConsultationOutcomeKind.Results, outcomeProfile, ranked,
            $"Tìm thấy {ranked.Count} sản phẩm đáp ứng các điều kiện đã xác nhận.", timer.ElapsedMilliseconds, aiAssisted);
    }

    private static int FindLastPhrasePosition(string input, string phrase)
    {
        var normalizedInput = ConsultationNameNormalizer.Normalize(input);
        var normalizedPhrase = ConsultationNameNormalizer.Normalize(phrase);
        var matches = Regex.Matches(normalizedInput,
            $@"(?<![a-z0-9]){Regex.Escape(normalizedPhrase)}(?![a-z0-9])",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        return matches.Count == 0 ? -1 : matches[matches.Count - 1].Index;
    }

    private static IReadOnlyList<RecommendedProduct> RankNeutralMatches(
        IReadOnlyList<ConsultationProduct> candidates,
        string? brandName,
        string categoryName,
        RequirementProfile profile) => candidates
        .OrderBy(product => profile.Budget.TargetVnd is decimal target
            ? Math.Abs((product.DiscountPrice ?? product.Price) - target)
            : product.DiscountPrice ?? product.Price)
        .ThenBy(product => product.Id)
        .Select(product => new RecommendedProduct(
            product,
            0m,
            [brandName is null
                ? $"Thuộc danh mục {categoryName} theo yêu cầu của bạn."
                : $"Thuộc thương hiệu {brandName} theo yêu cầu của bạn."],
            [],
            []))
        .ToArray();

    private static RequirementProfile ApplyAiInterpretation(
        RequirementProfile profile,
        ConsultationAiInterpretation interpretation,
        IReadOnlyList<ConsultationNeedOption> allowedUsageNeeds)
    {
        var category = profile.Category;
        if (category.State == RecognitionState.Missing &&
            CategoryNames.ContainsKey(interpretation.CategoryName ?? string.Empty))
        {
            category = new RecognizedField(interpretation.CategoryName, null, RecognitionState.Normalized);
        }

        var usage = profile.UsageNeed;
        var usageOption = allowedUsageNeeds.FirstOrDefault(item =>
            string.Equals(item.Code, interpretation.UsageNeedCode, StringComparison.OrdinalIgnoreCase));
        if (usage.State == RecognitionState.Missing && usageOption is not null)
        {
            usage = new RecognizedField(usageOption.Code, null, RecognitionState.Normalized);
        }

        var categoryResolved = category.Value is not null;
        var usageResolved = usage.Value is not null;
        var missingFields = profile.MissingFields
            .Where(field => !(categoryResolved && field == "danh mục") &&
                            !(usageResolved && field == "mục đích sử dụng"))
            .ToArray();
        var questions = profile.ClarificationQuestions
            .Where(question => !(categoryResolved && question.StartsWith("Bạn muốn tìm laptop", StringComparison.OrdinalIgnoreCase)) &&
                               !(usageResolved && question.StartsWith("Bạn dùng thiết bị chủ yếu", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return profile with
        {
            Category = category,
            UsageNeed = usage,
            MissingFields = missingFields,
            ClarificationQuestions = questions
        };
    }

    private static RequirementProfile ApplyConfirmations(RequirementProfile profile, ConsultationRequest request)
    {
        var category = string.IsNullOrWhiteSpace(request.ConfirmedCategory)
            ? profile.Category
            : new RecognizedField(request.ConfirmedCategory.Trim(), request.ConfirmedCategory.Trim(), RecognitionState.Explicit);
        var usage = string.IsNullOrWhiteSpace(request.ConfirmedUsage)
            ? profile.UsageNeed
            : new RecognizedField(request.ConfirmedUsage.Trim().ToUpperInvariant(), request.ConfirmedUsage.Trim(), RecognitionState.Explicit);
        var acceptedHardConstraints = request.IsAffirmativeReply
            ? profile.HardConstraints.Where(constraint => CanAcceptConfirmedConstraint(profile, request.PreviousAssistantMessage, constraint)).ToArray()
            : [];
        var acceptedConstraintKeys = acceptedHardConstraints.Select(GetConstraintKey).ToHashSet(StringComparer.Ordinal);
        var hardConstraints = profile.HardConstraints.Select(constraint =>
            acceptedConstraintKeys.Contains(GetConstraintKey(constraint))
                ? constraint with { State = RecognitionState.Normalized }
                : constraint).ToArray();
        var questions = profile.ClarificationQuestions
            .Where(question => !(question.Contains("tìm laptop", StringComparison.OrdinalIgnoreCase) && category.Value is not null) &&
                               !(question.Contains("mục đích sử dụng", StringComparison.OrdinalIgnoreCase) && usage.Value is not null) &&
                               !(question.StartsWith("Bạn dùng thiết bị", StringComparison.OrdinalIgnoreCase) && usage.Value is not null) &&
                               !acceptedHardConstraints.Any(constraint => MentionsConstraint(question, constraint)))
            .ToArray();
        var missing = profile.MissingFields
            .Where(field => !(field == "danh mục" && category.Value is not null) &&
                            !(field == "mục đích sử dụng" && usage.Value is not null))
            .ToArray();
        var warnings = profile.Warnings
            .Where(warning => !acceptedHardConstraints.Any(constraint =>
                string.Equals(
                    ConsultationNameNormalizer.Normalize(warning),
                    ConsultationNameNormalizer.Normalize($"Ngưỡng {constraint.SpecificationName} chưa có toán tử rõ ràng."),
                    StringComparison.Ordinal)))
            .ToArray();
        return profile with
        {
            Category = category,
            UsageNeed = usage,
            HardConstraints = hardConstraints,
            ClarificationQuestions = questions,
            MissingFields = missing,
            Warnings = warnings
        };
    }

    private static bool CanAcceptConfirmedConstraint(
        RequirementProfile profile,
        string? previousAssistantMessage,
        HardSpecificationConstraint constraint)
    {
        if (constraint.State != RecognitionState.Uncertain || string.IsNullOrWhiteSpace(previousAssistantMessage)) return false;
        if (profile.Warnings.Any(warning => warning.Contains("mâu thuẫn", StringComparison.OrdinalIgnoreCase))) return false;

        var normalizedName = ConsultationNameNormalizer.Normalize(constraint.SpecificationName);
        var hasMissingOperatorWarning = profile.Warnings.Any(warning => string.Equals(
            ConsultationNameNormalizer.Normalize(warning),
            ConsultationNameNormalizer.Normalize($"Ngưỡng {constraint.SpecificationName} chưa có toán tử rõ ràng."),
            StringComparison.Ordinal));
        var hasMissingUnitWarning = profile.Warnings.Any(warning => string.Equals(
            ConsultationNameNormalizer.Normalize(warning),
            ConsultationNameNormalizer.Normalize($"Thiếu đơn vị cho ngưỡng {constraint.SpecificationName}."),
            StringComparison.Ordinal));
        var hasIncompatibleUnitWarning = profile.Warnings.Any(warning =>
            ConsultationNameNormalizer.Normalize(warning).Contains(normalizedName, StringComparison.Ordinal) &&
            warning.Contains("không tương thích", StringComparison.OrdinalIgnoreCase));
        if (!hasMissingOperatorWarning || hasMissingUnitWarning || hasIncompatibleUnitWarning) return false;

        var previous = ConsultationNameNormalizer.Normalize(previousAssistantMessage);
        var askedForConfirmation = previous.Contains("dung khong", StringComparison.Ordinal) ||
                                   previous.Contains("xac nhan", StringComparison.Ordinal);
        return askedForConfirmation && MentionsConstraint(previousAssistantMessage, constraint);
    }

    private static bool MentionsConstraint(string text, HardSpecificationConstraint constraint)
    {
        var normalized = ConsultationNameNormalizer.Normalize(text);
        var name = ConsultationNameNormalizer.Normalize(constraint.SpecificationName);
        var value = constraint.Value.ToString("0.############################", CultureInfo.InvariantCulture);
        var valuePattern = $@"(?<!\d){Regex.Escape(value)}(?:[.,]0+)?(?!\d)";
        return normalized.Contains(name, StringComparison.Ordinal) &&
               Regex.IsMatch(normalized, valuePattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static string GetConstraintKey(HardSpecificationConstraint constraint) =>
        $"{ConsultationNameNormalizer.Normalize(constraint.SpecificationName)}:{constraint.Value.ToString("0.############################", CultureInfo.InvariantCulture)}";
}
