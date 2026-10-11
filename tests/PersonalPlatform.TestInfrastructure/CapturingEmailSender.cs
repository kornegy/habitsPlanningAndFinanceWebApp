using System.Collections.Concurrent;
using PersonalPlatform.Infrastructure.Email;

namespace PersonalPlatform.TestInfrastructure;

/// <summary>Records outgoing emails instead of sending them.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyCollection<EmailMessage> Messages => _messages;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string address) =>
        _messages.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Waits (bounded) for an email to <paramref name="address"/> whose body contains <paramref name="marker"/>.</summary>
    public async Task<EmailMessage> WaitForAsync(string address, string marker, int expectedCount = 1, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var matching = To(address).Where(m => m.TextBody.Contains(marker, StringComparison.Ordinal)).ToList();
            if (matching.Count >= expectedCount)
            {
                return matching[expectedCount - 1];
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"No email containing '{marker}' arrived.");
    }

    /// <summary>Extracts userId and token from a link of the form .../page#userId=...&amp;token=...</summary>
    public static (Guid UserId, string Token) ParseLink(EmailMessage message, string page)
    {
        var start = message.TextBody.IndexOf($"/{page}#", StringComparison.Ordinal);
        var fragment = message.TextBody[(start + page.Length + 2)..].Split(['\n', '\r', ' '], 2)[0];
        var parts = fragment.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        return (Guid.Parse(parts["userId"]), parts["token"]);
    }
}
