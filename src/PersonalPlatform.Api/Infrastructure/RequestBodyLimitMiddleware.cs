using Microsoft.AspNetCore.Mvc;

namespace PersonalPlatform.Api.Infrastructure;

/// <summary>
/// Server-independent request body limit (Kestrel's own limit is configured too, but does not apply behind
/// every hosting model). Requests announcing a larger Content-Length are refused before any processing;
/// bodies without Content-Length (chunked) are cut off when they exceed the limit while being read.
/// </summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next)
{
    public const long MaxBodyBytes = 1024 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        var length = context.Request.ContentLength;
        if (length > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status413PayloadTooLarge,
                    Title = "Request body is too large.",
                    Extensions = { ["code"] = "payload_too_large" },
                },
            });
            return;
        }

        if (length is null && context.Request.Body.CanRead)
        {
            context.Request.Body = new LimitedReadStream(context.Request.Body, MaxBodyBytes);
        }

        await next(context);
    }

    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            _read += read;
            if (_read > limit)
            {
                throw new BadHttpRequestException("Request body is too large.", StatusCodes.Status413PayloadTooLarge);
            }

            return read;
        }
    }
}
