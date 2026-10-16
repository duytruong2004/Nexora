using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Consultation;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Consultation;

public sealed class ConsultationManagementService(AppDbContext db) : IConsultationManagementService
{
    public async Task<IReadOnlyList<UsageNeedAdminRow>> GetUsageNeedsAsync(CancellationToken cancellationToken = default) =>
        await db.UsageNeeds.AsNoTracking()
            .OrderBy(item => item.Name)
            .Select(item => new UsageNeedAdminRow(
                item.Id, item.Code, item.Name, item.Description, item.IsActive, item.Rules.Count))
            .ToListAsync(cancellationToken);

    public async Task<int> SaveUsageNeedAsync(UsageNeedInput input, CancellationToken cancellationToken = default)
    {
        var code = input.Code.Trim().ToUpperInvariant();
        var name = input.Name.Trim();
        if (!Regex.IsMatch(code, @"^[A-Z][A-Z0-9_]{1,79}$", RegexOptions.CultureInvariant))
        {
            throw new ConsultationValidationException(nameof(input.Code), "Mã nhu cầu chỉ gồm A-Z, 0-9, dấu gạch dưới và bắt đầu bằng chữ cái (2–80 ký tự).");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
        {
            throw new ConsultationValidationException(nameof(input.Name), "Tên nhu cầu bắt buộc và tối đa 160 ký tự.");
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        if (normalizedDescription?.Length > 1000)
        {
            throw new ConsultationValidationException(nameof(input.Description), "Mô tả tối đa 1.000 ký tự.");
        }

        var duplicate = await db.UsageNeeds.AnyAsync(item => item.Code == code && item.Id != input.Id, cancellationToken);
        if (duplicate) throw new ConsultationValidationException(nameof(input.Code), "Mã nhu cầu đã tồn tại.");

        UsageNeed entity;
        if (input.Id is int id)
        {
            entity = await db.UsageNeeds.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new ConsultationValidationException(nameof(input.Id), "Không tìm thấy nhu cầu cần sửa.");
        }
        else
        {
            entity = new UsageNeed();
            db.UsageNeeds.Add(entity);
        }

        entity.Code = code;
        entity.Name = name;
        entity.Description = normalizedDescription;
        entity.IsActive = input.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<IReadOnlyList<RecommendationRuleAdminRow>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        await (from rule in db.RecommendationRules.AsNoTracking()
               join category in db.Categories.AsNoTracking() on rule.CategoryId equals category.Id
               orderby rule.UsageNeed.Name, category.Name, rule.Specification.Name
               select new RecommendationRuleAdminRow(
                rule.Id,
                rule.UsageNeedId,
                rule.UsageNeed.Name,
                rule.CategoryId,
                category.Name,
                rule.SpecificationId,
                rule.Specification.Name,
                rule.Specification.Unit,
                rule.Specification.DataType,
                rule.Operator,
                rule.ThresholdValue,
                rule.Weight,
                rule.IsRequired,
                rule.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<RecommendationRuleFormOptions> GetRuleFormOptionsAsync(CancellationToken cancellationToken = default)
    {
        var usageNeeds = await db.UsageNeeds.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new ConsultationAdminOption(item.Id, item.Name, item.Code))
            .ToListAsync(cancellationToken);
        var categories = await db.Categories.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new ConsultationAdminOption(item.Id, item.Name))
            .ToListAsync(cancellationToken);
        var specifications = await db.CategorySpecifications.AsNoTracking()
            .Where(item => item.Category.IsActive && item.Specification.IsActive)
            .OrderBy(item => item.Category.Name)
            .ThenBy(item => item.SortOrder)
            .Select(item => new ConsultationAdminOption(
                item.SpecificationId,
                item.Specification.Name,
                $"{item.CategoryId}|{item.Category.Name}|{item.Specification.DataType}|{item.Specification.Unit}"))
            .ToListAsync(cancellationToken);
        return new RecommendationRuleFormOptions(usageNeeds, categories, specifications);
    }

    public async Task<int> SaveRuleAsync(RecommendationRuleInput input, CancellationToken cancellationToken = default)
    {
        var operatorValue = input.Operator.Trim().ToUpperInvariant();
        var threshold = input.ThresholdValue.Trim();
        if (threshold.Length is < 1 or > 300)
        {
            throw new ConsultationValidationException(nameof(input.ThresholdValue), "Ngưỡng bắt buộc và tối đa 300 ký tự.");
        }

        if (!RecommendationRuleOperators.All.Contains(operatorValue))
        {
            throw new ConsultationValidationException(nameof(input.Operator), "Toán tử không hợp lệ.");
        }

        if (input.Weight is < 0 or > 99_999.9999m)
        {
            throw new ConsultationValidationException(nameof(input.Weight), "Trọng số phải nằm trong [0, 99.999,9999].");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        var savedId = 0;
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var usageNeed = await db.UsageNeeds.SingleOrDefaultAsync(item => item.Id == input.UsageNeedId, cancellationToken)
                ?? throw new ConsultationValidationException(nameof(input.UsageNeedId), "Nhu cầu tư vấn không tồn tại.");
            var category = await db.Categories.SingleOrDefaultAsync(item => item.Id == input.CategoryId, cancellationToken)
                ?? throw new ConsultationValidationException(nameof(input.CategoryId), "Danh mục không tồn tại.");
            var specification = await db.Specifications.SingleOrDefaultAsync(item => item.Id == input.SpecificationId, cancellationToken)
                ?? throw new ConsultationValidationException(nameof(input.SpecificationId), "Specification không tồn tại.");
            var assigned = await db.CategorySpecifications.AnyAsync(
                item => item.CategoryId == input.CategoryId && item.SpecificationId == input.SpecificationId,
                cancellationToken);
            if (!assigned)
            {
                throw new ConsultationValidationException(nameof(input.SpecificationId), "Specification phải được gán cho Category đã chọn.");
            }

            if (input.IsActive && (!usageNeed.IsActive || !category.IsActive || !specification.IsActive))
            {
                throw new ConsultationValidationException(nameof(input.IsActive), "Chỉ bật rule khi nhu cầu, Category và Specification đều đang hoạt động.");
            }

            var entity = input.Id is int id
                ? await db.RecommendationRules.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
                    ?? throw new ConsultationValidationException(nameof(input.Id), "Không tìm thấy rule cần sửa.")
                : new RecommendationRule();

            if (input.IsActive)
            {
                var duplicate = await db.RecommendationRules.AnyAsync(item =>
                    item.Id != entity.Id && item.IsActive &&
                    item.UsageNeedId == input.UsageNeedId && item.CategoryId == input.CategoryId &&
                    item.SpecificationId == input.SpecificationId, cancellationToken);
                if (duplicate)
                {
                    throw new ConsultationValidationException(nameof(input.SpecificationId),
                        "Đã có rule hoạt động cho cùng nhu cầu, danh mục và Specification. Hãy sửa rule hiện tại.");
                }
            }

            entity.UsageNeedId = input.UsageNeedId;
            entity.CategoryId = input.CategoryId;
            entity.SpecificationId = input.SpecificationId;
            entity.Operator = operatorValue;
            entity.ThresholdValue = threshold;
            entity.Weight = input.Weight;
            entity.IsRequired = input.IsRequired;
            entity.IsActive = input.IsActive;
            if (entity.Id == 0) db.RecommendationRules.Add(entity);

            var policyError = RecommendationRulePolicy.Validate(entity, specification);
            if (policyError is not null)
            {
                throw new ConsultationValidationException(nameof(input.ThresholdValue), policyError);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            savedId = entity.Id;
        });
        return savedId;
    }

    public async Task<bool> SetUsageNeedActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        var item = await db.UsageNeeds.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (item is null) return false;
        item.IsActive = isActive;
        if (!isActive)
        {
            var activeRules = await db.RecommendationRules.Where(rule => rule.UsageNeedId == id && rule.IsActive)
                .ToListAsync(cancellationToken);
            foreach (var rule in activeRules) rule.IsActive = false;
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetRecommendationRuleActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        var item = await db.RecommendationRules.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (item is null) return false;
        if (isActive)
        {
            var related = await db.UsageNeeds.Where(need => need.Id == item.UsageNeedId).Select(need => need.IsActive).SingleAsync(cancellationToken);
            var categoryActive = await db.Categories.Where(category => category.Id == item.CategoryId).Select(category => category.IsActive).SingleAsync(cancellationToken);
            var specification = await db.Specifications.SingleAsync(value => value.Id == item.SpecificationId, cancellationToken);
            if (!related || !categoryActive || !specification.IsActive)
            {
                throw new ConsultationValidationException(nameof(isActive), "Không thể bật rule vì một thành phần liên quan đang ngừng hoạt động.");
            }

            var duplicate = await db.RecommendationRules.AnyAsync(rule => rule.Id != id && rule.IsActive &&
                rule.UsageNeedId == item.UsageNeedId && rule.CategoryId == item.CategoryId &&
                rule.SpecificationId == item.SpecificationId, cancellationToken);
            if (duplicate) throw new ConsultationValidationException(nameof(isActive), "Đã có rule tương ứng đang hoạt động.");
            var policyError = RecommendationRulePolicy.Validate(item, specification);
            if (policyError is not null) throw new ConsultationValidationException(nameof(isActive), policyError);
        }
        item.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
