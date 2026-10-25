using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NEXORA.Application.Catalog;
using NEXORA.Application.Consultation;
using NEXORA.Web.Models;

namespace NEXORA.Web.Controllers;

[Route("tu-van-san-pham")]
public sealed class ConsultationController(
    IConsultationService consultation,
    IRequirementParser requirementParser,
    IConsultationDataSource consultationData,
    ICustomerCatalogQueryService catalog,
    IConsultationAiAssistant aiAssistant,
    ILogger<ConsultationController> logger) : Controller
{
    private static readonly JsonSerializerOptions ChatJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SupportedCategoryOrder =
    [
        "Laptop", "Điện thoại", "Máy tính bảng", "Màn hình", "Bàn phím", "Chuột", "Tai nghe",
        "Đồng hồ thông minh", "Máy ảnh", "Phụ kiện công nghệ"
    ];
    private static readonly HashSet<string> SupportedCategories = new(SupportedCategoryOrder, StringComparer.Ordinal);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View("Index", await BuildModelAsync(new CustomerConsultationViewModel(), null, cancellationToken));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(CustomerConsultationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildModelAsync(model, null, cancellationToken));
        }

        var outcome = await consultation.ConsultAsync(
            new ConsultationRequest(model.Description, model.ConfirmedCategory, model.ConfirmedUsage), cancellationToken);
        logger.LogInformation("Consultation request completed with outcome {OutcomeKind}, result count {ResultCount}, elapsed {ElapsedMilliseconds} ms.",
            outcome.Kind, outcome.Recommendations.Count, outcome.ElapsedMilliseconds);
        return View("Index", await BuildModelAsync(model, outcome, cancellationToken));
    }

    [HttpGet("chat-status")]
    public IActionResult ChatStatus() => Json(new
    {
        qwenConfigured = aiAssistant.IsAvailable,
        historyAvailable = false
    });

    [HttpGet("history")]
    [Authorize]
    public IActionResult History() => Json(Array.Empty<object>());

    [HttpGet("history/{id:guid}")]
    [Authorize]
    public IActionResult HistoryDetails(Guid id) => StatusCode(StatusCodes.Status410Gone,
        new { message = "Lịch sử tư vấn đã tắt; cuộc trò chuyện chỉ tồn tại trong phiên trang hiện tại." });

    [HttpDelete("history/{id:guid}")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteHistory(Guid id) => StatusCode(StatusCodes.Status410Gone,
        new { message = "Lịch sử tư vấn đã tắt; không có cuộc trò chuyện nào được lưu trên máy chủ." });

    [HttpPost("chat")]
    [EnableRateLimiting("consultation-chat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Chat([FromBody] ConsultationChatRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var message = request.Message.Trim();
        if (message.Length == 0) return BadRequest(new { message = "Hãy nhập nhu cầu trước khi gửi." });

        // A broad catalog search starts a fresh topic so an old brand/model does not leak into it.
        // Keep prior requirements when the user is answering a category clarification.
        var currentProfile = requirementParser.Parse(message).Profile;
        var isBrandAvailabilityQuestion = IsBrandAvailabilityQuestion(message);
        var isAnswerToCategoryClarification = IsCategoryClarification(request.PreviousAssistantMessage);
        var startsNewTopic = isBrandAvailabilityQuestion || IsBroadCatalogSearch(message) ||
            (currentProfile.Category.Value is not null && !isAnswerToCategoryClarification);

        // Chat context stays in the current browser page. Never read or write conversation history in SQL.
        var previousUserMessages = startsNewTopic
            ? Array.Empty<string>()
            : (request.PreviousUserMessages ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).TakeLast(6).ToArray();
        var previousProductIds = startsNewTopic
            ? Array.Empty<int>()
            : (request.PreviousRecommendedProductIds ?? []).Where(id => id > 0).Distinct().Take(5).ToArray();
        var previousAssistantMessage = startsNewTopic ? null : request.PreviousAssistantMessage;
        var transcript = BuildTranscript(previousUserMessages, message);

        string assistantText;
        IReadOnlyList<ConsultationProductCardResponse> products = [];
        object? comparison = null;
        var aiAssisted = false;
        var recommendationIdsForContext = previousProductIds.Take(5).ToArray();
        var namedComparison = TryParseNamedComparison(message);
        var compareIntent = IsComparisonIntent(message);

        if (IsConsultationCapabilityQuestion(message))
        {
            assistantText = $"Hiện NEXORA hỗ trợ tư vấn {SupportedCategoryOrder.Length} nhóm: {string.Join(", ", SupportedCategoryOrder)}. Bạn có thể nêu thẳng tên sản phẩm, chẳng hạn “bàn phím”, để mình tìm đúng danh mục.";
        }
        else if (IsAccessoryAvailabilityQuestion(message))
        {
            assistantText = "NEXORA có các nhóm thiết bị và phụ kiện như bàn phím, chuột, tai nghe, màn hình và phụ kiện công nghệ. Bạn muốn xem nhóm nào? Khi tìm, mình sẽ chỉ đưa sản phẩm có trong danh mục NEXORA.";
        }
        else if (isBrandAvailabilityQuestion)
        {
            var allBrands = await catalog.SearchAsync(
                new CatalogSearchFilter(null, null, null, null, null, "name-asc", PageSize: 1),
                cancellationToken);
            var requestedBrand = FindRequestedBrand(message, allBrands.Brands);
            if (requestedBrand is null)
            {
                assistantText = allBrands.Brands.Count == 0
                    ? "Hiện catalog NEXORA chưa có thương hiệu hoạt động để tra cứu."
                    : "Bạn muốn tìm sản phẩm của thương hiệu nào? Hãy cho mình tên hãng, ví dụ Apple hoặc Samsung nhé.";
            }
            else
            {
                var brandProducts = await catalog.SearchAsync(
                    new CatalogSearchFilter(null, null, requestedBrand.Id, null, null, "name-asc", PageSize: 5),
                    cancellationToken);
                if (brandProducts.TotalCount == 0)
                {
                    assistantText = $"NEXORA hiện chưa có sản phẩm đang hiển thị thuộc thương hiệu {requestedBrand.Name}.";
                }
                else
                {
                    assistantText = $"Có {brandProducts.TotalCount} sản phẩm {requestedBrand.Name} trong catalog NEXORA. Mình hiển thị tối đa 5 sản phẩm bên dưới; thông tin lấy từ catalog.";
                    products = brandProducts.Items.Select((item, index) => new ConsultationProductCardResponse(
                        item.Id,
                        item.Name,
                        item.CategoryName,
                        item.BrandName,
                        item.Price,
                        item.DiscountPrice,
                        item.PrimaryImageUrl,
                        item.AvailableQuantity,
                        item.KeySpecifications.Where(spec => !string.IsNullOrWhiteSpace(spec.DisplayValue) || !string.IsNullOrWhiteSpace(spec.Value))
                            .Take(3).Select(spec => new ConsultationSpecificationResponse(spec.Name, spec.DisplayValue ?? spec.Value, spec.Unit)).ToArray(),
                        [$"Thuộc thương hiệu {requestedBrand.Name} theo catalog NEXORA."],
                        [],
                        Url.Action("Details", "Catalog", new { id = item.Id }) ?? $"/Catalog/Details/{item.Id}",
                        index + 1)).ToArray();
                    recommendationIdsForContext = brandProducts.Items.Select(item => item.Id).ToArray();
                }
            }
        }
        else if (namedComparison is not null)
        {
            var firstMatches = await FindComparisonProductsAsync(namedComparison.First, cancellationToken);
            var secondMatches = await FindComparisonProductsAsync(namedComparison.Second, cancellationToken);

            if (firstMatches.Count == 0 || secondMatches.Count == 0)
            {
                assistantText = $"Mình chưa tìm thấy đủ đúng hai mẫu “{namedComparison.First}” và “{namedComparison.Second}” trong danh mục NEXORA để so sánh. Mình không thay bằng mẫu khác.";
            }
            else if (firstMatches.Count > 1 || secondMatches.Count > 1)
            {
                var ambiguousTarget = firstMatches.Count > 1 ? namedComparison.First : namedComparison.Second;
                assistantText = $"NEXORA có nhiều phiên bản của “{ambiguousTarget}”. Bạn cho mình biết dung lượng hoặc phiên bản cụ thể để so sánh chính xác nhé.";
            }
            else if (firstMatches[0].Id == secondMatches[0].Id)
            {
                assistantText = "Hai tên bạn nêu đang khớp cùng một sản phẩm trong catalog. Bạn kiểm tra lại tên hoặc phiên bản thứ hai giúp mình nhé.";
            }
            else
            {
                try
                {
                    var result = await catalog.CompareAsync(
                        [firstMatches[0].Id, secondMatches[0].Id], cancellationToken);
                    recommendationIdsForContext = result.Products.Select(item => item.Id).ToArray();
                    comparison = new
                    {
                        products = result.Products.Select(item => new { item.Id, item.Name, item.BrandName, item.Price, item.DiscountPrice }),
                        rows = result.Specifications.Select(row => new { row.Name, row.Unit, row.Values, row.IsDifferent })
                    };
                    assistantText = $"Mình đã đối chiếu đúng hai mẫu “{result.Products[0].Name}” và “{result.Products[1].Name}” theo dữ liệu NEXORA. Bảng bên dưới chỉ thể hiện thông tin có trong catalog; ô thiếu là dữ liệu chưa được cung cấp.";
                }
                catch (CatalogValidationException)
                {
                    assistantText = "Hai mẫu tìm được không thể so sánh cùng nhau trong catalog. Mình chưa thay thế bằng sản phẩm khác.";
                }
            }
        }
        else if (compareIntent && previousProductIds.Length >= 2)
        {
            try
            {
                var result = await catalog.CompareAsync(previousProductIds.Take(2).ToArray(), cancellationToken);
                recommendationIdsForContext = previousProductIds.Take(2).ToArray();
                comparison = new
                {
                    products = result.Products.Select(item => new { item.Id, item.Name, item.BrandName, item.Price, item.DiscountPrice }),
                    rows = result.Specifications.Select(row => new { row.Name, row.Unit, row.Values, row.IsDifferent })
                };
                assistantText = "Mình đã lấy lại hai sản phẩm gần nhất và đối chiếu thông số có trong NEXORA. Ô trống nghĩa là dữ liệu hiện chưa có.";
            }
            catch (CatalogValidationException)
            {
                assistantText = "Hai sản phẩm gần nhất không thể so sánh cùng nhau. Bạn hãy mở trang sản phẩm và chọn các mẫu cùng danh mục.";
            }
        }
        else
        {
            var outcome = await consultation.ConsultAsync(new ConsultationRequest(
                transcript,
                PreviousAssistantMessage: previousAssistantMessage,
                IsAffirmativeReply: IsAffirmativeReply(message)), cancellationToken);
            var assistantContext = BuildAssistantContext(
                previousUserMessages, previousAssistantMessage, message, currentProfile, outcome.Profile,
                isBrandAvailabilityQuestion, startsNewTopic, isAnswerToCategoryClarification, previousProductIds);
            var hasIPhoneConstraint = ContainsIPhoneRequest(transcript);
            var connectivityRequirement = ConsultationProductRequirementMatcher.GetConnectivityRequirement(message);
            var qwenResponse = hasIPhoneConstraint ||
                connectivityRequirement is not null ||
                outcome.Kind is ConsultationOutcomeKind.ClarificationRequired or ConsultationOutcomeKind.NoMatchingProducts
                ? null
                : await aiAssistant.ComposeResponseAsync(
                    assistantContext,
                    outcome,
                    (chunk, token) => WriteChatEventAsync("delta", new { text = chunk }, token),
                    cancellationToken);
            aiAssisted = outcome.AiAssisted || qwenResponse is not null;
            assistantText = connectivityRequirement is not null
                ? BuildConnectivityResponse(outcome, connectivityRequirement.Value)
                : qwenResponse ?? (hasIPhoneConstraint && outcome.Kind == ConsultationOutcomeKind.ClarificationRequired
                ? $"Mình chưa thể xác nhận iPhone trong danh mục NEXORA khi chưa có kết quả phù hợp. {outcome.Profile.ClarificationQuestions.FirstOrDefault() ?? outcome.Message}"
                : BuildAssistantText(outcome));
            var recommendations = outcome.Recommendations.Take(5).ToArray();
            if (recommendations.Length > 0) recommendationIdsForContext = recommendations.Select(item => item.Product.Id).ToArray();
            products = recommendations.Select((item, index) => new ConsultationProductCardResponse(
                item.Product.Id,
                item.Product.Name,
                item.Product.CategoryName,
                item.Product.BrandName,
                item.Product.Price,
                item.Product.DiscountPrice,
                item.Product.PrimaryImageUrl,
                item.Product.AvailableQuantity,
                item.Product.Specifications.Where(spec => !string.IsNullOrWhiteSpace(spec.DisplayValue) || !string.IsNullOrWhiteSpace(spec.Value))
                    .Take(3).Select(spec => new ConsultationSpecificationResponse(spec.Name, spec.DisplayValue ?? spec.Value, spec.Unit)).ToArray(),
                item.Reasons.Take(3).ToArray(),
                item.Limitations.Take(2).ToArray(),
                Url.Action("Details", "Catalog", new { id = item.Product.Id }) ?? $"/Catalog/Details/{item.Product.Id}",
                index + 1)).ToArray();
        }

        await WriteChatEventAsync("result", new
        {
            conversationId = (Guid?)null,
            role = "assistant",
            message = assistantText,
            aiAssisted,
            products,
            comparison,
            previousUserMessages = previousUserMessages.Append(message).TakeLast(6),
            recommendedProductIds = recommendationIdsForContext
        }, cancellationToken);
        return new EmptyResult();
    }

    private async ValueTask WriteChatEventAsync(string eventName, object payload, CancellationToken cancellationToken)
    {
        if (!Response.HasStarted)
        {
            Response.ContentType = "text/event-stream; charset=utf-8";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";
        }

        await Response.WriteAsync(
            $"event: {eventName}\ndata: {JsonSerializer.Serialize(payload, ChatJsonOptions)}\n\n",
            cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private async Task<CustomerConsultationViewModel> BuildModelAsync(
        CustomerConsultationViewModel model,
        ConsultationOutcome? outcome,
        CancellationToken cancellationToken)
    {
        var home = await catalog.GetHomeAsync(cancellationToken);
        var usageNeeds = await consultationData.GetActiveUsageNeedsAsync(cancellationToken);
        var selectedCategory = model.ConfirmedCategory ?? outcome?.Profile.Category.Value;
        var selectedUsage = model.ConfirmedUsage;
        if (selectedUsage is null && outcome?.Profile.UsageNeed.Value is string profileUsage)
        {
            selectedUsage = usageNeeds.FirstOrDefault(item => item.Code == profileUsage || item.Name == profileUsage)?.Code;
        }

        model.ConfirmedCategory = selectedCategory;
        model.ConfirmedUsage = selectedUsage;
        model.Outcome = outcome;
        model.CategoryOptions = home.Categories
            .Where(item => SupportedCategories.Contains(item.Name))
            .OrderBy(item => item.Name)
            .Select(item => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(item.Name, item.Name,
                string.Equals(item.Name, selectedCategory, StringComparison.Ordinal)))
            .ToArray();
        model.UsageOptions = usageNeeds.Select(item => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(
            item.Name, item.Code, item.Code == selectedUsage)).ToArray();
        return model;
    }

    private static bool IsAffirmativeReply(string message)
    {
        var normalized = ConsultationNameNormalizer.Normalize(message)
            .Trim(' ', '.', '!', '?', ',', ';', ':', '…');
        return normalized is "dung" or "dung roi" or "dung a" or "dung roi a" or
            "chinh xac" or "vang" or "vang a" or "da dung" or "ok" or "okay" or "duoc" or "chuan";
    }

    private static bool IsComparisonIntent(string input)
    {
        var normalized = ConsultationNameNormalizer.Normalize(input);
        return normalized.Contains("so sanh", StringComparison.Ordinal) ||
               normalized.Contains("doi chieu", StringComparison.Ordinal) ||
               normalized.Contains("khac nhau", StringComparison.Ordinal) ||
               normalized.Contains("khac biet", StringComparison.Ordinal) ||
               Regex.IsMatch(normalized, @"\b(?:vs|versus)\b", RegexOptions.CultureInvariant,
                   TimeSpan.FromMilliseconds(100));
    }

    private static NamedComparison? TryParseNamedComparison(string input)
    {
        var normalized = ConsultationNameNormalizer.Normalize(input);
        var match = Regex.Match(normalized,
            @"(?:\bso sanh\b|\bkhac nhau\b|\bkhac biet\b)\s+(?:giua\s+)?(?<first>.+?)\s+\b(?:va|voi|vs|versus|and)\b\s+(?<second>.+?)\s*$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) return null;

        var first = NormalizeComparisonTarget(match.Groups["first"].Value);
        var second = NormalizeComparisonTarget(match.Groups["second"].Value);
        return first.Length >= 3 && second.Length >= 3 &&
               !string.Equals(first, second, StringComparison.Ordinal)
            ? new NamedComparison(first, second)
            : null;
    }

    private async Task<IReadOnlyList<CatalogProductCard>> FindComparisonProductsAsync(
        string requestedName,
        CancellationToken cancellationToken)
    {
        var page = await catalog.SearchAsync(
            new CatalogSearchFilter(requestedName, null, null, null, null, "name-asc", PageSize: 48),
            cancellationToken);
        return page.Items
            .Where(item => ConsultationNameNormalizer.Normalize(item.Name)
                .Contains(requestedName, StringComparison.Ordinal))
            .ToArray();
    }

    private static string NormalizeComparisonTarget(string value)
    {
        var normalized = Regex.Replace(value.Trim(), @"[?.!,;:]+$", string.Empty,
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim();
        normalized = Regex.Replace(normalized,
            @"\s+(?:khac nhau|khac biet|nhu the nao|giup toi|nhe)$",
            string.Empty, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100)).Trim();
        return Regex.Replace(normalized, @"\s+", " ", RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }

    private sealed record NamedComparison(string First, string Second);

    private static bool ContainsIPhoneRequest(string input)
    {
        var normalized = ConsultationNameNormalizer.Normalize(input);
        return Regex.IsMatch(normalized, @"(?<![a-z0-9])i\s*phone(?![a-z0-9])",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static bool IsBroadCatalogSearch(string input)
    {
        var normalized = ConsultationNameNormalizer.Normalize(input);
        return Regex.IsMatch(normalized,
            @"\b(?:san pham nao|co san pham|tim san pham|san pham tam|cac san pham|phu kien|ban phim|keyboard|chuot|mouse|tai nghe|headphone|headset|man hinh may tinh|monitor|dong ho thong minh|smartwatch|may anh|camera|adapter|cu sac)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static bool IsConsultationCapabilityQuestion(string input)
    {
        var normalized = ConsultationNameNormalizer.Normalize(input);
        var asksAboutConsultation = normalized.Contains("tu van", StringComparison.Ordinal) ||
                                    normalized.Contains("ho tro", StringComparison.Ordinal);
        var asksForScope = normalized.Contains("bao nhieu loai", StringComparison.Ordinal) ||
                           normalized.Contains("nhung loai nao", StringComparison.Ordinal) ||
                           normalized.Contains("danh muc nao", StringComparison.Ordinal) ||
                           normalized.Contains("ho tro nhung gi", StringComparison.Ordinal);
        return asksAboutConsultation && asksForScope;
    }

    private static bool IsAccessoryAvailabilityQuestion(string input)
    {
        var normalized = ConsultationNameNormalizer.Normalize(input);
        return normalized.Contains("phu kien", StringComparison.Ordinal) &&
               Regex.IsMatch(normalized,
                   @"\b(?:co|hien co|san pham)\b.*\b(?:khong|nao)\b",
                   RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static bool IsBrandAvailabilityQuestion(string input)
    {
        var normalized = ConsultationNameNormalizer.Normalize(input);
        if (normalized.Contains("thuong hieu", StringComparison.Ordinal) ||
            normalized.Contains("hang nao", StringComparison.Ordinal)) return true;

        if (Regex.IsMatch(normalized,
                @"\b(?:co|hien co|cua hang co|ban co|tim|xem)\b.*\b(?:san pham|mau)\b.*\bcua\b|\b(?:san pham|mau)\b.*\bcua\b",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return true;

        var mentionsAppleFamily = new[] { "apple", "iphone", "i phone", "ipad", "macbook" }
            .Any(alias => FindPhrasePosition(normalized, alias) >= 0);
        return mentionsAppleFamily && Regex.IsMatch(normalized,
            @"\b(?:co|hien co|cua hang|san pham|mau|tim|xem|thuong hieu|hang)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static CatalogBrandOption? FindRequestedBrand(
        string input,
        IReadOnlyList<CatalogBrandOption> activeBrands)
    {
        var normalizedInput = ConsultationNameNormalizer.Normalize(input);
        var matches = new List<(CatalogBrandOption Brand, int Position, int Length)>();

        foreach (var brand in activeBrands)
        {
            var normalizedBrand = ConsultationNameNormalizer.Normalize(brand.Name);
            var position = FindPhrasePosition(normalizedInput, normalizedBrand);
            if (position >= 0) matches.Add((brand, position, normalizedBrand.Length));

            if (string.Equals(normalizedBrand, "apple", StringComparison.Ordinal))
            {
                foreach (var alias in new[] { "iphone", "i phone", "ipad", "macbook" })
                {
                    position = FindPhrasePosition(normalizedInput, alias);
                    if (position >= 0) matches.Add((brand, position, alias.Length));
                }
            }
        }

        return matches.OrderByDescending(item => item.Position)
            .ThenByDescending(item => item.Length)
            .Select(item => item.Brand)
            .FirstOrDefault();
    }

    private static int FindPhrasePosition(string normalizedInput, string normalizedPhrase)
    {
        if (normalizedPhrase.Length == 0) return -1;
        var match = Regex.Match(normalizedInput,
            $@"(?<![a-z0-9]){Regex.Escape(normalizedPhrase)}(?![a-z0-9])",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return match.Success ? match.Index : -1;
    }

    private static bool IsCategoryClarification(string? assistantMessage)
    {
        if (string.IsNullOrWhiteSpace(assistantMessage)) return false;
        var normalized = ConsultationNameNormalizer.Normalize(assistantMessage);
        return normalized.Contains("ban muon tim nhom san pham nao", StringComparison.Ordinal) ||
               normalized.Contains("ban muon tim laptop dien thoai hay may tinh bang", StringComparison.Ordinal) ||
               normalized.Contains("chon danh muc", StringComparison.Ordinal);
    }

    private static string BuildTranscript(IReadOnlyList<string> previous, string current)
    {
        const int maximumCharacters = 1200;
        const string previousMarker = "Lượt trước: ";
        const string currentMarker = "Tin nhắn hiện tại: ";
        var currentText = current.Trim();
        var currentTurn = currentText.Length + currentMarker.Length <= maximumCharacters
            ? currentMarker + currentText
            : currentText;
        var result = new List<string>();
        var total = currentTurn.Length;
        for (var index = previous.Count - 1; index >= 0; index--)
        {
            var turn = previous[index].Trim();
            if (turn.Length == 0) continue;
            var remaining = maximumCharacters - total - 1 - previousMarker.Length;
            if (remaining <= 0) break;
            if (turn.Length > remaining) turn = turn[^remaining..];
            result.Add(previousMarker + turn);
            total += turn.Length + previousMarker.Length + 1;
        }
        result.Reverse();
        result.Add(currentTurn);
        return string.Join("\n", result);
    }

    private static string BuildAssistantContext(
        IReadOnlyList<string> previousUserMessages,
        string? previousAssistantMessage,
        string currentMessage,
        RequirementProfile currentProfile,
        RequirementProfile resolvedProfile,
        bool isBrandAvailabilityQuestion,
        bool startsNewTopic,
        bool isAnswerToCategoryClarification,
        IReadOnlyList<int> previousProductIds)
    {
        static string? Bound(string? value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= maximumLength ? trimmed : $"…{trimmed[^maximumLength..]}";
        }

        return JsonSerializer.Serialize(new
        {
            conversation_state = new
            {
                current_intent = isBrandAvailabilityQuestion ? "brand_availability" :
                    IsComparisonIntent(currentMessage) ? "product_comparison" :
                    currentProfile.Category.Value is not null ? "product_search" : "needs_clarification_or_general_question",
                starts_new_topic = startsNewTopic,
                answers_previous_category_question = isAnswerToCategoryClarification,
                carry_forward_previous_constraints = !startsNewTopic,
                referenced_product_ids = previousProductIds
            },
            current_requirements = new
            {
                category = currentProfile.Category.Value,
                usage_code = currentProfile.UsageNeed.Value,
                budget_kind = currentProfile.Budget.Kind.ToString(),
                minimum_vnd = currentProfile.Budget.MinimumVnd,
                maximum_vnd = currentProfile.Budget.MaximumVnd,
                target_vnd = currentProfile.Budget.TargetVnd,
                hard_constraints = currentProfile.HardConstraints.Select(item => new
                {
                    name = item.SpecificationName,
                    item.Operator,
                    item.Value,
                    item.Unit,
                    state = item.State.ToString()
                })
            },
            resolved_requirements = new
            {
                category = resolvedProfile.Category.Value,
                usage_code = resolvedProfile.UsageNeed.Value,
                budget_kind = resolvedProfile.Budget.Kind.ToString(),
                minimum_vnd = resolvedProfile.Budget.MinimumVnd,
                maximum_vnd = resolvedProfile.Budget.MaximumVnd,
                target_vnd = resolvedProfile.Budget.TargetVnd,
                missing_fields = resolvedProfile.MissingFields,
                clarification_questions = resolvedProfile.ClarificationQuestions,
                hard_constraints = resolvedProfile.HardConstraints.Select(item => new
                {
                    name = item.SpecificationName,
                    item.Operator,
                    item.Value,
                    item.Unit,
                    state = item.State.ToString()
                })
            },
            previous_user_messages = previousUserMessages.TakeLast(3)
                .Select(item => Bound(item, 350)).Where(item => item is not null).ToArray(),
            previous_assistant_message = Bound(previousAssistantMessage, 350),
            current_user_message = currentMessage
        }, ChatJsonOptions);
    }

    private static string BuildAssistantText(ConsultationOutcome outcome) => outcome.Kind switch
    {
        ConsultationOutcomeKind.Results => $"Mình đã đối chiếu nhu cầu với danh mục sản phẩm hiện có và tìm thấy {outcome.Recommendations.Take(5).Count()} lựa chọn phù hợp. Thông tin giá, ảnh và cấu hình bên dưới được lấy từ NEXORA.",
        ConsultationOutcomeKind.NoMatchingProducts => $"{outcome.Message} Bạn có thể nới ngân sách hoặc bỏ một yêu cầu cấu hình bắt buộc để mình tìm gần hơn.",
        ConsultationOutcomeKind.ClarificationRequired => string.Join("\n", new[] { outcome.Message }.Concat(outcome.Profile.ClarificationQuestions.Take(3))),
        _ => outcome.Message
    };

    private static string BuildConnectivityResponse(
        ConsultationOutcome outcome,
        ProductConnectivityRequirement requirement)
    {
        var requestedType = requirement == ProductConnectivityRequirement.Wireless ? "không dây" : "có dây";
        if (outcome.Kind == ConsultationOutcomeKind.Results)
        {
            var shownCount = Math.Min(5, outcome.Recommendations.Count);
            return $"Mình tìm thấy {shownCount} sản phẩm có thông tin trong NEXORA khớp tiêu chí {requestedType}. Các thẻ bên dưới là những kết quả phù hợp.";
        }

        var alternativeType = requirement == ProductConnectivityRequirement.Wireless ? "có dây" : "không dây";
        return $"Hiện chưa tìm thấy sản phẩm có dữ liệu xác nhận tiêu chí {requestedType} trong danh mục này. Mình không thay bằng mẫu {alternativeType}.";
    }

    public sealed class ConsultationChatRequest
    {
        [Required, StringLength(1200, MinimumLength = 1)]
        public string Message { get; set; } = string.Empty;

        public Guid? ConversationId { get; set; }

        public List<string>? PreviousUserMessages { get; set; }

        public List<int>? PreviousRecommendedProductIds { get; set; }

        [StringLength(500)]
        public string? PreviousAssistantMessage { get; set; }
    }

    private sealed record ConsultationSpecificationResponse(string Name, string Value, string? Unit);

    private sealed record ConsultationProductCardResponse(
        int Id,
        string Name,
        string CategoryName,
        string BrandName,
        decimal Price,
        decimal? DiscountPrice,
        string? ImageUrl,
        int? AvailableQuantity,
        IReadOnlyList<ConsultationSpecificationResponse> Specifications,
        IReadOnlyList<string> Reasons,
        IReadOnlyList<string> Limitations,
        string DetailsUrl,
        int Rank);
}
