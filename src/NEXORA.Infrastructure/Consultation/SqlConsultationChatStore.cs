using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Consultation;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Consultation;

/// <summary>Persists only authenticated users' chats and scopes every read/write by owner id.</summary>
public sealed class SqlConsultationChatStore(AppDbContext db) : IConsultationChatStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ConsultationChatSummary>> GetRecentAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        await db.ConsultationConversations.AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(30)
            .Select(item => new ConsultationChatSummary(item.Id, item.Title, item.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<ConsultationChatHistory?> GetAsync(
        int userId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await db.ConsultationConversations.AsNoTracking()
            .Include(item => item.Messages)
            .SingleOrDefaultAsync(item => item.Id == conversationId && item.UserId == userId, cancellationToken);
        return conversation is null ? null : ToHistory(conversation);
    }

    public async Task<Guid> AppendExchangeAsync(
        int userId,
        Guid? conversationId,
        string userMessage,
        string assistantMessage,
        IReadOnlyList<int> recommendedProductIds,
        CancellationToken cancellationToken = default)
    {
        ConsultationConversation conversation;
        if (conversationId is Guid existingId)
        {
            conversation = await db.ConsultationConversations
                .Include(item => item.Messages)
                .SingleOrDefaultAsync(item => item.Id == existingId && item.UserId == userId, cancellationToken)
                ?? throw new ConsultationConversationNotFoundException();
        }
        else
        {
            var title = new string(userMessage.Where(character => !char.IsControl(character)).ToArray()).Trim();
            if (title.Length > 157) title = title[..157].TrimEnd() + "…";
            conversation = new ConsultationConversation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = string.IsNullOrWhiteSpace(title) ? "Cuộc trò chuyện mới" : title,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.ConsultationConversations.Add(conversation);
        }

        var now = DateTime.UtcNow;
        conversation.UpdatedAt = now;
        conversation.Messages.Add(new ConsultationChatMessage
        {
            Role = "user",
            Content = userMessage,
            CreatedAt = now
        });
        conversation.Messages.Add(new ConsultationChatMessage
        {
            Role = "assistant",
            Content = assistantMessage,
            RecommendedProductIdsJson = JsonSerializer.Serialize(recommendedProductIds.Distinct().Take(5), JsonOptions),
            CreatedAt = now.AddTicks(1)
        });
        await db.SaveChangesAsync(cancellationToken);
        return conversation.Id;
    }

    public async Task<bool> DeleteAsync(
        int userId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await db.ConsultationConversations
            .SingleOrDefaultAsync(item => item.Id == conversationId && item.UserId == userId, cancellationToken);
        if (conversation is null) return false;
        db.ConsultationConversations.Remove(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static ConsultationChatHistory ToHistory(ConsultationConversation conversation) => new(
        conversation.Id,
        conversation.Title,
        conversation.CreatedAt,
        conversation.UpdatedAt,
        conversation.Messages.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
            .Select(item => new ConsultationChatMessageItem(
                item.Role,
                item.Content,
                item.CreatedAt,
                ReadRecommendedIds(item.RecommendedProductIdsJson)))
            .ToArray());

    private static IReadOnlyList<int> ReadRecommendedIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        try
        {
            return JsonSerializer.Deserialize<int[]>(value, JsonOptions)?.Where(id => id > 0).Take(5).ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
