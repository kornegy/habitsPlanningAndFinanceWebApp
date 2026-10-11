using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace PersonalPlatform.Infrastructure.Email;

/// <summary>Plain-text email. Bodies never contain HTML, so user-controlled values cannot inject markup.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody);

public enum EmailDeliveryMode
{
    /// <summary>Send through an SMTP server (required outside Development).</summary>
    Smtp = 0,

    /// <summary>Write .eml files to a local directory (local development without a mail server).</summary>
    PickupDirectory = 1,
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailDeliveryMode Mode { get; set; } = EmailDeliveryMode.Smtp;

    [Required]
    [EmailAddress]
    public string FromAddress { get; set; } = "no-reply@localhost";

    public string FromName { get; set; } = "Personal Platform";

    public string SmtpHost { get; set; } = "localhost";

    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 587;

    /// <summary>Use STARTTLS (or implicit TLS on port 465). Only disable for a local test relay such as Mailpit.</summary>
    public bool UseTls { get; set; } = true;

    public string? SmtpUsername { get; set; }

    public string? SmtpPassword { get; set; }

    public string PickupDirectory { get; set; } = ".mail";

    /// <summary>Bounded queue size; when full, new messages are dropped (and logged) instead of exhausting memory.</summary>
    [Range(10, 100_000)]
    public int QueueCapacity { get; set; } = 1000;
}

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Bounded in-memory queue. Account endpoints enqueue and return immediately, so response timing does not
/// reveal whether an email was sent (i.e. whether an account exists).
/// </summary>
public sealed class EmailQueue
{
    private readonly Channel<EmailMessage> _channel;
    private readonly ILogger<EmailQueue> _logger;

    public EmailQueue(IOptions<EmailOptions> options, ILogger<EmailQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });
    }

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Enqueue(EmailMessage message)
    {
        if (!_channel.Writer.TryWrite(message))
        {
            _logger.LogWarning("Email queue is full; message dropped");
        }
    }
}

public sealed class EmailDispatcher(EmailQueue queue, IEmailSender sender, ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await sender.SendAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or SmtpCommandException or SmtpProtocolException or AuthenticationException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
            {
                // Recipient address and body are intentionally not logged.
                logger.LogError(ex, "Email delivery failed ({ExceptionType})", ex.GetType().Name);
            }
        }
    }
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var o = options.Value;
        using var client = new SmtpClient { Timeout = (int)SendTimeout.TotalMilliseconds };
        var security = !o.UseTls
            ? SecureSocketOptions.None
            : o.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(o.SmtpHost, o.SmtpPort, security, cancellationToken);
        if (!string.IsNullOrEmpty(o.SmtpUsername))
        {
            await client.AuthenticateAsync(o.SmtpUsername, o.SmtpPassword ?? string.Empty, cancellationToken);
        }

        using var mime = EmailMime.Build(message, o);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

public sealed class PickupDirectoryEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var o = options.Value;
        Directory.CreateDirectory(o.PickupDirectory);
        var path = Path.Combine(o.PickupDirectory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");
        using var mime = EmailMime.Build(message, o);
        await mime.WriteToAsync(path, cancellationToken);
    }
}

internal static class EmailMime
{
    public static MimeMessage Build(EmailMessage message, EmailOptions options)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.TextBody };
        return mime;
    }
}
