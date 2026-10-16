using System.Diagnostics;

namespace NEXORA.Application.Consultation;

public sealed class ConsultationOrchestrator(
    IRequirementParser parser,
    IConsultationDataSource dataSource) : IConsultationService
{
    private static readonly IReadOnlyDictionary<string, string> CategoryNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Laptop"] = "Laptop",
        ["Điện thoại"] = "Điện thoại",
        ["Máy tính bảng"] = "Máy tính bảng"
    };

    public async Task<ConsultationOutcome> ConsultAsync(
        ConsultationRequest request,
        CancellationToken cancellationToken = default)
    {
        var parsed = parser.Parse(request.Description);
        if (!parsed.IsValid)
        {
            return new ConsultationOutcome(ConsultationOutcomeKind.InvalidInput, parsed.Profile, [], parsed.Error!);
        }

        var profile = ApplyConfirmations(parsed.Profile, request);
        var questions = profile.ClarificationQuestions.ToList();
        if (profile.Category.Value is null)
        {
            questions.Add("Bạn muốn tìm laptop, điện thoại hay máy tính bảng?");
        }

        if (profile.UsageNeed.Value is null)
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
                    .Concat(profile.Category.Value is null ? ["danh mục"] : [])
                    .Concat(profile.UsageNeed.Value is null ? ["mục đích sử dụng"] : [])
                    .Distinct(StringComparer.Ordinal).ToArray(),
                ClarificationQuestions = questions.Distinct(StringComparer.Ordinal).ToArray()
            };
            return new ConsultationOutcome(ConsultationOutcomeKind.ClarificationRequired, clarificationProfile, [],
                "Cần xác nhận một số thông tin trước khi tìm sản phẩm.");
        }

        if (profile.Category.Value is null || !CategoryNames.ContainsKey(profile.Category.Value) || profile.UsageNeed.Value is null)
        {
            return new ConsultationOutcome(ConsultationOutcomeKind.ClarificationRequired, profile, [],
                "Danh mục hoặc mục đích chưa nằm trong phạm vi tư vấn hiện tại.");
        }

        var timer = Stopwatch.StartNew();
        var context = await dataSource.GetContextAsync(
            CategoryNames[profile.Category.Value], profile.UsageNeed.Value, cancellationToken);
        if (context is null)
        {
            return new ConsultationOutcome(ConsultationOutcomeKind.ClarificationRequired, profile, [],
                "Danh mục hoặc nhu cầu này chưa có cấu hình tư vấn hoạt động. Vui lòng chọn nhu cầu khác hoặc liên hệ quản trị viên.");
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
                    "Không thể áp dụng điều kiện kỹ thuật với dữ liệu hiện có.");
            }

            numericConstraints.Add((definition.Id, constraint.Operator, constraint.Value));
        }

        var budget = profile.Budget;
        var query = new CandidateQuery(
            context.CategoryId,
            budget.Kind == BudgetKind.Range ? budget.MinimumVnd : null,
            budget.Kind is BudgetKind.Maximum or BudgetKind.Range ? budget.MaximumVnd : null,
            numericConstraints);
        var candidates = await dataSource.FindCandidatesAsync(query, cancellationToken);
        var ranked = RecommendationScoringEngine.Rank(context, candidates, profile);
        timer.Stop();

        if (ranked.Count == 0)
        {
            return new ConsultationOutcome(ConsultationOutcomeKind.NoMatchingProducts, profile, [],
                "Không có sản phẩm đang kinh doanh đáp ứng toàn bộ điều kiện đã xác nhận. Hãy tự điều chỉnh ngân sách hoặc thông số bắt buộc rồi thử lại.",
                timer.ElapsedMilliseconds);
        }

        var outcomeProfile = profile with
        {
            Category = new RecognizedField(context.CategoryName, profile.Category.OriginalText, profile.Category.State),
            UsageNeed = new RecognizedField(context.UsageNeedName, profile.UsageNeed.OriginalText, profile.UsageNeed.State)
        };
        return new ConsultationOutcome(ConsultationOutcomeKind.Results, outcomeProfile, ranked,
            $"Tìm thấy {ranked.Count} sản phẩm đáp ứng các điều kiện đã xác nhận.", timer.ElapsedMilliseconds);
    }

    private static RequirementProfile ApplyConfirmations(RequirementProfile profile, ConsultationRequest request)
    {
        var category = string.IsNullOrWhiteSpace(request.ConfirmedCategory)
            ? profile.Category
            : new RecognizedField(request.ConfirmedCategory.Trim(), request.ConfirmedCategory.Trim(), RecognitionState.Explicit);
        var usage = string.IsNullOrWhiteSpace(request.ConfirmedUsage)
            ? profile.UsageNeed
            : new RecognizedField(request.ConfirmedUsage.Trim().ToUpperInvariant(), request.ConfirmedUsage.Trim(), RecognitionState.Explicit);
        var questions = profile.ClarificationQuestions
            .Where(question => !(question.Contains("tìm laptop", StringComparison.OrdinalIgnoreCase) && category.Value is not null) &&
                               !(question.Contains("mục đích sử dụng", StringComparison.OrdinalIgnoreCase) && usage.Value is not null) &&
                               !(question.StartsWith("Bạn dùng thiết bị", StringComparison.OrdinalIgnoreCase) && usage.Value is not null))
            .ToArray();
        var missing = profile.MissingFields
            .Where(field => !(field == "danh mục" && category.Value is not null) &&
                            !(field == "mục đích sử dụng" && usage.Value is not null))
            .ToArray();
        return profile with { Category = category, UsageNeed = usage, ClarificationQuestions = questions, MissingFields = missing };
    }
}
