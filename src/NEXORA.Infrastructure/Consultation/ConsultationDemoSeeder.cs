using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Consultation;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Consultation;

/// <summary>Additive Development-only consultation configuration; existing rows are never overwritten.</summary>
public sealed class ConsultationDemoSeeder(AppDbContext db, ILogger<ConsultationDemoSeeder> logger) : IConsultationDemoSeeder
{
    private const string DemoPrefix = "Cấu hình mô phỏng NEXORA Development; ngưỡng dùng để kiểm thử thuật toán, không phải khuyến nghị kỹ thuật của nhà sản xuất.";

    private static readonly (string Code, string Name, string Description)[] Needs =
    [
        ("OFFICE", "Văn phòng", $"{DemoPrefix} Phục vụ tình huống sử dụng văn phòng."),
        ("STUDY", "Học tập", $"{DemoPrefix} Phục vụ tình huống học tập và học trực tuyến."),
        ("PROGRAMMING", "Lập trình", $"{DemoPrefix} Phục vụ tình huống lập trình."),
        ("GAMING", "Chơi game", $"{DemoPrefix} Phục vụ tình huống chơi game."),
        ("GRAPHICS", "Đồ họa", $"{DemoPrefix} Phục vụ tình huống thiết kế đồ họa."),
        ("PHOTO_EDITING", "Chỉnh sửa ảnh", $"{DemoPrefix} Phục vụ tình huống chỉnh sửa ảnh."),
        ("VIDEO_EDITING", "Chỉnh sửa video", $"{DemoPrefix} Phục vụ tình huống chỉnh sửa video."),
        ("GENERAL", "Nhu cầu thông thường", $"{DemoPrefix} Phục vụ nhu cầu sử dụng thông thường.")
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var insertedNeeds = 0;
        var insertedRules = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var needsByCode = await db.UsageNeeds.ToDictionaryAsync(item => item.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
            foreach (var (code, name, description) in Needs)
            {
                if (needsByCode.ContainsKey(code)) continue;
                var item = new UsageNeed { Code = code, Name = name, Description = description, IsActive = true };
                db.UsageNeeds.Add(item);
                needsByCode.Add(code, item);
                insertedNeeds++;
            }
            await db.SaveChangesAsync(cancellationToken);

            var categories = await db.Categories.AsNoTracking()
                .Where(item => item.IsActive && item.ParentId == null &&
                               (item.Name == "Laptop" || item.Name == "Điện thoại" || item.Name == "Máy tính bảng"))
                .ToListAsync(cancellationToken);
            foreach (var category in categories)
            {
                var assignments = await db.CategorySpecifications.AsNoTracking()
                    .Where(item => item.CategoryId == category.Id && item.Specification.IsActive)
                    .Select(item => new
                    {
                        item.SpecificationId,
                        item.Specification.Name,
                        item.Specification.DataType,
                        item.Specification.Unit
                    })
                    .ToListAsync(cancellationToken);
                foreach (var (code, _, _) in Needs)
                {
                    var need = needsByCode[code];
                    if (!need.IsActive) continue;
                    foreach (var assignment in assignments)
                    {
                        if (!TryCreateRule(code, category.Name, assignment.Name, assignment.DataType,
                                out var operation, out var threshold, out var weight, out var required))
                        {
                            continue;
                        }

                        var exists = await db.RecommendationRules.AnyAsync(rule =>
                            rule.UsageNeedId == need.Id && rule.CategoryId == category.Id &&
                            rule.SpecificationId == assignment.SpecificationId, cancellationToken);
                        if (exists) continue;

                        var rule = new RecommendationRule
                        {
                            UsageNeedId = need.Id,
                            CategoryId = category.Id,
                            SpecificationId = assignment.SpecificationId,
                            Operator = operation,
                            ThresholdValue = threshold,
                            Weight = weight,
                            IsRequired = required,
                            IsActive = true
                        };
                        var specification = new Specification
                        {
                            Name = assignment.Name,
                            DataType = assignment.DataType,
                            Unit = assignment.Unit,
                            IsActive = true
                        };
                        if (RecommendationRulePolicy.Validate(rule, specification) is not null) continue;
                        db.RecommendationRules.Add(rule);
                        insertedRules++;
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        logger.LogInformation(
            "Development consultation seed checked. Added {UsageNeedCount} usage needs and {RuleCount} recommendation rules; existing rows were preserved.",
            insertedNeeds, insertedRules);
    }

    private static bool TryCreateRule(
        string usageCode,
        string categoryName,
        string specificationName,
        string dataType,
        out string operation,
        out string threshold,
        out decimal weight,
        out bool required)
    {
        operation = RecommendationRuleOperators.GreaterThanOrEqual;
        threshold = string.Empty;
        weight = 0;
        required = false;

        var intensive = usageCode is "PROGRAMMING" or "GAMING" or "GRAPHICS" or "PHOTO_EDITING" or "VIDEO_EDITING";
        if (categoryName == "Laptop")
        {
            if (specificationName == "RAM" && dataType == SpecificationDataTypes.Number)
            {
                threshold = intensive ? "16" : "8";
                required = true;
                return true;
            }

            if (specificationName == "SSD" && dataType == SpecificationDataTypes.Number)
            {
                threshold = intensive && usageCode is ("PHOTO_EDITING" or "VIDEO_EDITING") ? "1024" : "512";
                weight = usageCode == "PROGRAMMING" ? 4m : intensive ? 2m : 1m;
                return true;
            }

            if (specificationName == "Trọng lượng" && dataType == SpecificationDataTypes.Number)
            {
                operation = RecommendationRuleOperators.LessThanOrEqual;
                threshold = "1.8";
                weight = usageCode == "OFFICE" ? 3m : 1m;
                return true;
            }

            if (specificationName == "Kích thước màn hình" && dataType == SpecificationDataTypes.Number &&
                usageCode is ("GRAPHICS" or "VIDEO_EDITING"))
            {
                threshold = "15.6";
                weight = 1m;
                return true;
            }

            if (specificationName == "GPU" && dataType == SpecificationDataTypes.Enumeration && intensive && usageCode != "PROGRAMMING")
            {
                operation = RecommendationRuleOperators.Equal;
                threshold = "Demo GPU Dedicated";
                weight = 2m;
                return true;
            }
        }
        else if (categoryName is "Điện thoại" or "Máy tính bảng")
        {
            var tablet = categoryName == "Máy tính bảng";
            if (specificationName == "RAM" && dataType == SpecificationDataTypes.Number)
            {
                threshold = tablet ? (intensive ? "8" : "6") : (intensive ? "12" : "8");
                required = true;
                return true;
            }

            if (specificationName == "Bộ nhớ lưu trữ" && dataType == SpecificationDataTypes.Number)
            {
                threshold = intensive ? "256" : "128";
                weight = usageCode is "PROGRAMMING" or "OFFICE" ? 1m : 2m;
                return true;
            }

            if (specificationName == "Dung lượng pin" && dataType == SpecificationDataTypes.Number)
            {
                threshold = tablet ? (intensive ? "8500" : "7000") : (intensive ? "5000" : "4500");
                weight = 3m;
                return true;
            }

            if (specificationName == "Kích thước màn hình" && dataType == SpecificationDataTypes.Number && tablet)
            {
                threshold = "11";
                weight = 1m;
                return true;
            }
        }

        return false;
    }
}
