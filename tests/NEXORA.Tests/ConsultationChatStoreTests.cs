using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Consultation;
using NEXORA.Infrastructure.Consultation;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Tests;

public sealed class ConsultationChatStoreTests
{
    [Fact]
    public async Task HistoryAndDelete_AreScopedToTheOwningUser()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options);
        db.Users.AddRange(User(82), User(83));
        await db.SaveChangesAsync();
        var store = new SqlConsultationChatStore(db);

        var conversationId = await store.AppendExchangeAsync(82, null,
            "Tìm laptop dưới 20 triệu", "Mình tìm thấy lựa chọn từ NEXORA.", [11, 12]);

        var owned = await store.GetAsync(82, conversationId);
        Assert.NotNull(owned);
        Assert.Equal("Tìm laptop dưới 20 triệu", owned!.Title);
        Assert.Equal(["user", "assistant"], owned.Messages.Select(item => item.Role));
        Assert.Equal([11, 12], owned.Messages[1].RecommendedProductIds);
        Assert.Null(await store.GetAsync(83, conversationId));
        Assert.False(await store.DeleteAsync(83, conversationId));
        Assert.NotNull(await store.GetAsync(82, conversationId));
        Assert.True(await store.DeleteAsync(82, conversationId));
        Assert.Null(await store.GetAsync(82, conversationId));
    }

    private static AppUser User(int id) => new()
    {
        Id = id,
        UserName = $"user{id}@nexora.test",
        NormalizedUserName = $"USER{id}@NEXORA.TEST",
        Email = $"user{id}@nexora.test",
        NormalizedEmail = $"USER{id}@NEXORA.TEST",
        FullName = $"Test user {id}",
        SecurityStamp = Guid.NewGuid().ToString("N")
    };
}
