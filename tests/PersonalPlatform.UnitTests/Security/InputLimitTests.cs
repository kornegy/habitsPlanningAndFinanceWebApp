using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using PersonalPlatform.Api.Infrastructure;
using PersonalPlatform.Infrastructure.Email;

namespace PersonalPlatform.UnitTests.Security;

public sealed class RequestBodyLimitTests
{
    [Fact]
    public async Task Announced_oversized_bodies_are_refused_before_the_endpoint_runs()
    {
        var context = Context(new MemoryStream(), RequestBodyLimitMiddleware.MaxBodyBytes + 1);
        var reached = false;

        await new RequestBodyLimitMiddleware(_ => { reached = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    [Fact]
    public async Task Chunked_bodies_are_cut_off_once_they_exceed_the_limit()
    {
        var context = Context(new MemoryStream(new byte[RequestBodyLimitMiddleware.MaxBodyBytes + 1]), contentLength: null);

        var error = await Assert.ThrowsAsync<BadHttpRequestException>(() =>
            new RequestBodyLimitMiddleware(async ctx => await ctx.Request.Body.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken)).InvokeAsync(context));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, error.StatusCode);
    }

    [Fact]
    public async Task Chunked_bodies_within_the_limit_are_read_completely()
    {
        var payload = new byte[RequestBodyLimitMiddleware.MaxBodyBytes];
        var context = Context(new MemoryStream(payload), contentLength: null);
        long read = 0;

        await new RequestBodyLimitMiddleware(async ctx =>
        {
            using var copy = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(copy, TestContext.Current.CancellationToken);
            read = copy.Length;
        }).InvokeAsync(context);

        Assert.Equal(payload.Length, read);
    }

    private static DefaultHttpContext Context(Stream body, long? contentLength)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = body;
        context.Request.ContentLength = contentLength;
        context.Response.Body = new MemoryStream();
        return context;
    }
}

public sealed class EmailMimeTests
{
    private static readonly EmailOptions Options = new() { FromAddress = "no-reply@example.test", FromName = "Personal Platform" };

    [Fact]
    public void Messages_are_plain_text_with_a_single_recipient()
    {
        using var mime = EmailMime.Build(new EmailMessage("user@example.test", "Confirm", "Hello <b>there</b>"), Options);

        var recipient = Assert.Single(mime.To.Mailboxes);
        Assert.Equal("user@example.test", recipient.Address);
        var body = Assert.IsType<TextPart>(mime.Body);
        Assert.True(body.IsPlain);
        Assert.Empty(mime.Bcc);
        Assert.Empty(mime.Cc);
    }

    [Theory]
    [InlineData("user@example.test\r\nBcc: victim@example.test")]
    [InlineData("user@example.test, other@example.test")]
    public void Recipient_header_injection_is_rejected(string to) =>
        Assert.ThrowsAny<Exception>(() =>
        {
            using var mime = EmailMime.Build(new EmailMessage(to, "Confirm", "x"), Options);
            Assert.Single(mime.To.Mailboxes);
        });

    [Fact]
    public void Line_breaks_in_the_subject_cannot_add_headers()
    {
        using var mime = EmailMime.Build(new EmailMessage("user@example.test", "Hi\r\nBcc: victim@example.test", "x"), Options);
        using var output = new MemoryStream();
        mime.WriteTo(output);
        var raw = System.Text.Encoding.ASCII.GetString(output.ToArray());

        Assert.DoesNotContain("\r\nBcc:", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("victim@example.test>", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pickup_directory_sender_writes_one_eml_file_per_message()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pp-mail-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sender = new PickupDirectoryEmailSender(Microsoft.Extensions.Options.Options.Create(new EmailOptions { PickupDirectory = directory }));

            await sender.SendAsync(new EmailMessage("user@example.test", "Confirm", "Link"), TestContext.Current.CancellationToken);

            var file = Assert.Single(Directory.GetFiles(directory, "*.eml"));
            Assert.Contains("To: user@example.test", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
