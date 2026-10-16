using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Consultation;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Consultation;
using NEXORA.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace NEXORA.Tests;

public sealed class ConsultationSqlServerIntegrationTests
{
    [SqlServerFact]
    public async Task CandidateQuery_FiltersBySqlPriceAndDynamicSpecification()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new AppDbContext(options);
        var category = await db.Categories.AsNoTracking().SingleAsync(item => item.Name == "Laptop" && item.IsActive);
        var ram = await db.CategorySpecifications.AsNoTracking()
            .Where(item => item.CategoryId == category.Id && item.Specification.Name == "RAM")
            .Select(item => new { item.SpecificationId, item.Specification.DataType, item.Specification.Unit })
            .SingleAsync();
        Assert.Equal(SpecificationDataTypes.Number, ram.DataType);
        Assert.Equal("GB", ram.Unit);

        var source = new SqlConsultationDataSource(db);
        var results = await source.FindCandidatesAsync(new CandidateQuery(
            category.Id, null, 25_000_000m,
            [(ram.SpecificationId, RecommendationRuleOperators.GreaterThanOrEqual, 16m)]));

        Assert.NotEmpty(results);
        Assert.All(results, product =>
        {
            Assert.True((product.DiscountPrice ?? product.Price) <= 25_000_000m);
            Assert.Contains(product.Specifications, item => item.SpecificationId == ram.SpecificationId && item.NumericValue >= 16m);
        });
    }

    [SqlServerFact]
    public async Task ConsultationConfiguration_IsLoadedFromSqlServerAndInvalidCategoryAssignmentIsRejected()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new AppDbContext(options);
        var source = new SqlConsultationDataSource(db);
        var context = await source.GetContextAsync("Laptop", "PROGRAMMING");

        Assert.NotNull(context);
        Assert.NotEmpty(context!.Rules);
        Assert.All(context.Rules, rule => Assert.Contains(context.Specifications, spec => spec.Id == rule.SpecificationId));

        var phoneCategory = await db.Categories.AsNoTracking().SingleAsync(item => item.Name == "Điện thoại");
        var phoneSpecificationId = await db.CategorySpecifications.AsNoTracking()
            .Where(item => item.CategoryId == phoneCategory.Id && item.Specification.Name == "Dung lượng pin")
            .Select(item => item.SpecificationId)
            .SingleAsync();
        var usageNeedId = context.UsageNeedId;
        var management = new ConsultationManagementService(db);

        var exception = await Assert.ThrowsAsync<ConsultationValidationException>(() => management.SaveRuleAsync(
            new RecommendationRuleInput(null, usageNeedId, context.CategoryId, phoneSpecificationId,
                RecommendationRuleOperators.Equal, "test", 1m, false, true)));
        Assert.Contains("được gán", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [SqlServerFact]
    public async Task DevelopmentSeed_IsAdditiveAndIdempotent()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new AppDbContext(options);
        var seeder = new ConsultationDemoSeeder(db, NullLogger<ConsultationDemoSeeder>.Instance);

        await seeder.SeedAsync();
        var usageCountAfterFirst = await db.UsageNeeds.CountAsync();
        var ruleCountAfterFirst = await db.RecommendationRules.CountAsync();
        await seeder.SeedAsync();
        var usageCountAfterSecond = await db.UsageNeeds.CountAsync();
        var ruleCountAfterSecond = await db.RecommendationRules.CountAsync();

        Assert.True(usageCountAfterFirst >= 8);
        Assert.True(ruleCountAfterFirst > 0);
        Assert.Equal(usageCountAfterFirst, usageCountAfterSecond);
        Assert.Equal(ruleCountAfterFirst, ruleCountAfterSecond);
    }

    [SqlServerFact]
    public async Task ConsultationAdminService_CreatesEditsDeactivatesAndCleansTestRows()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new AppDbContext(options);
        var service = new ConsultationManagementService(db);
        var category = await db.Categories.AsNoTracking().SingleAsync(item => item.Name == "Laptop");
        var ramId = await db.CategorySpecifications.AsNoTracking()
            .Where(item => item.CategoryId == category.Id && item.Specification.Name == "RAM")
            .Select(item => item.SpecificationId)
            .SingleAsync();
        var testCode = $"PHASE3_TEST_{Guid.NewGuid():N}"[..43].ToUpperInvariant();
        var usageId = 0;
        var ruleId = 0;

        try
        {
            usageId = await service.SaveUsageNeedAsync(new UsageNeedInput(
                null, testCode, "NEXORA Phase 3 QA need", "Temporary SQL Server CRUD test row.", true));
            Assert.Contains(await service.GetUsageNeedsAsync(), item => item.Id == usageId && item.IsActive);
            await service.SaveUsageNeedAsync(new UsageNeedInput(
                usageId, testCode, "NEXORA Phase 3 QA need edited", "Temporary SQL Server CRUD test row.", true));
            Assert.Contains(await service.GetUsageNeedsAsync(), item => item.Id == usageId && item.Name.EndsWith("edited", StringComparison.Ordinal));

            ruleId = await service.SaveRuleAsync(new RecommendationRuleInput(
                null, usageId, category.Id, ramId, RecommendationRuleOperators.GreaterThanOrEqual,
                "16", 2m, false, true));
            Assert.Contains(await service.GetRulesAsync(), item => item.Id == ruleId && item.Weight == 2m && item.IsActive);
            await service.SaveRuleAsync(new RecommendationRuleInput(
                ruleId, usageId, category.Id, ramId, RecommendationRuleOperators.GreaterThanOrEqual,
                "16", 3m, false, true));
            Assert.Contains(await service.GetRulesAsync(), item => item.Id == ruleId && item.Weight == 3m);
            Assert.True(await service.SetRecommendationRuleActiveAsync(ruleId, false));
            Assert.Contains(await service.GetRulesAsync(), item => item.Id == ruleId && !item.IsActive);
            Assert.True(await service.SetRecommendationRuleActiveAsync(ruleId, true));
            Assert.Contains(await service.GetRulesAsync(), item => item.Id == ruleId && item.IsActive);
            Assert.True(await service.SetUsageNeedActiveAsync(usageId, false));
            Assert.Contains(await service.GetUsageNeedsAsync(), item => item.Id == usageId && !item.IsActive);
            Assert.Contains(await service.GetRulesAsync(), item => item.Id == ruleId && !item.IsActive);
        }
        finally
        {
            db.ChangeTracker.Clear();
            var rule = await db.RecommendationRules.SingleOrDefaultAsync(item => item.Id == ruleId);
            if (rule is not null) db.RecommendationRules.Remove(rule);
            var need = await db.UsageNeeds.SingleOrDefaultAsync(item => item.Id == usageId);
            if (need is not null) db.UsageNeeds.Remove(need);
            await db.SaveChangesAsync();
        }
    }

    [SqlServerFact]
    public async Task RecommendationRuleWeightChange_ChangesDemoProductOrderWithoutChangingPrice()
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXORA_SQLSERVER_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new AppDbContext(options);
        var service = new ConsultationManagementService(db);
        var source = new SqlConsultationDataSource(db);
        var category = await db.Categories.AsNoTracking().SingleAsync(item => item.Name == "Laptop");
        var specificationIds = await db.CategorySpecifications.AsNoTracking()
            .Where(item => item.CategoryId == category.Id &&
                           (item.Specification.Name == "RAM" || item.Specification.Name == "Trọng lượng"))
            .Select(item => new { item.SpecificationId, item.Specification.Name })
            .ToDictionaryAsync(item => item.Name, item => item.SpecificationId);
        var usageId = 0;
        var ruleIds = new List<int>();

        try
        {
            var code = $"PHASE3_WEIGHT_{Guid.NewGuid():N}"[..43].ToUpperInvariant();
            usageId = await service.SaveUsageNeedAsync(new UsageNeedInput(null, code,
                "NEXORA Phase 3 weight QA", "Temporary ranking test configuration.", true));
            var ramRuleId = await service.SaveRuleAsync(new RecommendationRuleInput(null,
                usageId, category.Id, specificationIds["RAM"], RecommendationRuleOperators.GreaterThanOrEqual,
                "32", 1m, false, true));
            var weightRuleId = await service.SaveRuleAsync(new RecommendationRuleInput(null,
                usageId, category.Id, specificationIds["Trọng lượng"], RecommendationRuleOperators.LessThanOrEqual,
                "1.6", 3m, false, true));
            ruleIds.Add(ramRuleId);
            ruleIds.Add(weightRuleId);

            var query = new CandidateQuery(category.Id, null, null, []);
            var candidates = (await source.FindCandidatesAsync(query))
                .Where(item => item.Sku.StartsWith("NXD-LAP-", StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(candidates);
            var pricesBefore = candidates.ToDictionary(item => item.Id, item => item.Price);
            var firstContext = await source.GetContextAsync("Laptop", code);
            Assert.NotNull(firstContext);
            var profile = new RequirementProfile("NEXORA Phase 3 test",
                new RecognizedField("Laptop", null, RecognitionState.Explicit),
                new BudgetRequirement(BudgetKind.None, null, null, null, null, RecognitionState.Missing),
                new RecognizedField(code, null, RecognitionState.Explicit), [], [], [], [], []);
            var firstRanking = RecommendationScoringEngine.Rank(firstContext!, candidates, profile);

            await service.SaveRuleAsync(new RecommendationRuleInput(ramRuleId,
                usageId, category.Id, specificationIds["RAM"], RecommendationRuleOperators.GreaterThanOrEqual,
                "32", 4m, false, true));
            await service.SaveRuleAsync(new RecommendationRuleInput(weightRuleId,
                usageId, category.Id, specificationIds["Trọng lượng"], RecommendationRuleOperators.LessThanOrEqual,
                "1.6", 1m, false, true));
            var updatedContext = await source.GetContextAsync("Laptop", code);
            Assert.NotNull(updatedContext);
            var updatedRanking = RecommendationScoringEngine.Rank(updatedContext!, candidates, profile);

            Assert.NotEqual(firstRanking.Select(item => item.Product.Id), updatedRanking.Select(item => item.Product.Id));
            Assert.Equal(pricesBefore, candidates.ToDictionary(item => item.Id, item => item.Price));
        }
        finally
        {
            db.ChangeTracker.Clear();
            var rules = await db.RecommendationRules.Where(item => ruleIds.Contains(item.Id)).ToListAsync();
            db.RecommendationRules.RemoveRange(rules);
            var need = await db.UsageNeeds.SingleOrDefaultAsync(item => item.Id == usageId);
            if (need is not null) db.UsageNeeds.Remove(need);
            await db.SaveChangesAsync();
        }
    }
}
