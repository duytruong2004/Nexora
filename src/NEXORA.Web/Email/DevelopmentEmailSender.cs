using System.Collections.Concurrent;
using NEXORA.Application.Email;

namespace NEXORA.Web.Email;

public sealed record DevelopmentEmail(DateTimeOffset QueuedAt, EmailMessage Message);

public sealed class InMemoryEmailMailbox
{
    private const int MaximumMessages = 50;
    private readonly ConcurrentQueue<DevelopmentEmail> _messages = new();

    public void Enqueue(DevelopmentEmail message)
    {
        _messages.Enqueue(message);
        while (_messages.Count > MaximumMessages && _messages.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<DevelopmentEmail> GetRecent() => _messages
        .ToArray()
        .OrderByDescending(message => message.QueuedAt)
        .ToArray();
}

public sealed class DevelopmentEmailSender(InMemoryEmailMailbox mailbox) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        mailbox.Enqueue(new DevelopmentEmail(DateTimeOffset.UtcNow, message));
        return Task.CompletedTask;
    }
}

public sealed class UnconfiguredEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "No production email sender is configured. Configure an IEmailSender implementation before enabling password reset.");
}
