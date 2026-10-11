using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PersonalPlatform.Api.Infrastructure;

/// <summary>
/// Runs the registered FluentValidation validator for every action argument before the action executes.
/// Failures surface as <see cref="ValidationException"/> and are rendered as 400 problem details.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is IValidator validator)
            {
                var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
                if (!result.IsValid)
                {
                    throw new ValidationException(result.Errors);
                }
            }
        }

        await next();
    }
}
