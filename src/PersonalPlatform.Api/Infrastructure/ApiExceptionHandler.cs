using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PersonalPlatform.Application.Auth;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Api.Infrastructure;

/// <summary>
/// Maps exceptions to RFC 9457 problem details. Responses contain a stable <c>code</c> and a generic message
/// only: never stack traces, SQL, exception messages of infrastructure errors, or object details.
/// </summary>
public sealed partial class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499; // client closed request; nothing to send
            return true;
        }

        var problem = Map(exception);
        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception, exception.GetType().Name);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    internal static ProblemDetails Map(Exception exception) => exception switch
    {
        ValidationException validation => ValidationProblem(validation),
        DomainRuleException rule => Problem(StatusCodes.Status422UnprocessableEntity, "The request violates a business rule.", rule.Code, rule.Message),
        NotFoundException => Problem(StatusCodes.Status404NotFound, "Not found.", "not_found"),
        ConflictException conflict => Problem(StatusCodes.Status409Conflict, "Conflict.", conflict.Code, conflict.Message),
        UniqueConstraintException => Problem(StatusCodes.Status409Conflict, "An item with the same unique value already exists.", "duplicate"),
        AuthFailedException auth => auth.Code switch
        {
            "email_not_confirmed" => Problem(StatusCodes.Status403Forbidden, "Email address is not confirmed.", auth.Code),
            "invalid_token" => Problem(StatusCodes.Status400BadRequest, "The link is invalid or has expired.", auth.Code),
            _ => Problem(StatusCodes.Status401Unauthorized, "Authentication failed.", auth.Code),
        },
        BadHttpRequestException bad => Problem(bad.StatusCode, "Bad request.", "bad_request"),
        TimeoutException or OperationCanceledException => Problem(StatusCodes.Status503ServiceUnavailable, "The request timed out.", "timeout"),
        _ => Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "internal_error"),
    };

    private static ValidationProblemDetails ValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };
        problem.Extensions["code"] = "validation_failed";
        problem.Extensions["errorCodes"] = exception.Errors
            .Where(e => !string.IsNullOrEmpty(e.ErrorCode))
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorCode).Distinct().ToArray());
        return problem;
    }

    private static ProblemDetails Problem(int status, string title, string code, string? detail = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        return problem;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception {ExceptionType}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string exceptionType);
}
