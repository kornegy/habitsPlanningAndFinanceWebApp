using FluentValidation;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Tasks;
using PersonalPlatform.Domain.Scheduling;

namespace PersonalPlatform.Application.Common;

/// <summary>Reusable FluentValidation rules shared by request validators.</summary>
public static class ValidationRules
{
    public static readonly DateOnly MinDate = new(2000, 1, 1);
    public static readonly DateOnly MaxDate = new(2100, 12, 31);

    public static IRuleBuilderOptions<T, DateOnly> ReasonableDate<T>(this IRuleBuilder<T, DateOnly> rule) =>
        rule.InclusiveBetween(MinDate, MaxDate).WithMessage("Date must be between 2000-01-01 and 2100-12-31.");

    public static IRuleBuilderOptions<T, DateOnly?> ReasonableDate<T>(this IRuleBuilder<T, DateOnly?> rule) =>
        rule.Must(d => d is null || (d >= MinDate && d <= MaxDate)).WithMessage("Date must be between 2000-01-01 and 2100-12-31.");

    public static IRuleBuilderOptions<T, DateTimeOffset?> ReasonableInstant<T>(this IRuleBuilder<T, DateTimeOffset?> rule) =>
        rule.Must(d => d is null || (d.Value.Year >= MinDate.Year && d.Value.Year <= MaxDate.Year)).WithMessage("Time is out of the supported range.");

    public static IRuleBuilderOptions<T, DateTimeOffset> ReasonableInstant<T>(this IRuleBuilder<T, DateTimeOffset> rule) =>
        rule.Must(d => d.Year >= MinDate.Year && d.Year <= MaxDate.Year).WithMessage("Time is out of the supported range.");

    public static IRuleBuilderOptions<T, int> ValidPage<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, Paging.MaxPage);

    public static IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, Paging.MaxPageSize).WithMessage($"Page size must be between 1 and {Paging.MaxPageSize}.");

    public static IRuleBuilderOptions<T, string?> ValidSearch<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(QueryLimits.SearchMaxLength);

    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>?> DistinctIds<T>(this IRuleBuilder<T, IReadOnlyList<Guid>?> rule, int max) =>
        rule.Must(ids => ids is null || (ids.Count <= max && ids.Distinct().Count() == ids.Count && !ids.Contains(Guid.Empty)))
            .WithMessage($"Provide at most {max} distinct, non-empty ids.");

    /// <summary>Both bounds optional; when both are given, From ≤ To and the span is at most <paramref name="maxDays"/>.</summary>
    public static void ValidRange<T>(this AbstractValidator<T> validator, Func<T, DateOnly?> from, Func<T, DateOnly?> to, int maxDays)
    {
        validator.RuleFor(x => from(x)).ReasonableDate().OverridePropertyName("From");
        validator.RuleFor(x => to(x)).ReasonableDate().OverridePropertyName("To");
        validator.RuleFor(x => x)
            .Must(x => from(x) is not { } f || to(x) is not { } t || (f <= t && t.DayNumber - f.DayNumber <= maxDays))
            .WithMessage($"'From' must not be after 'To' and the range must not exceed {maxDays} days.")
            .OverridePropertyName("To");
    }
}

public sealed class RecurrenceDtoValidator : AbstractValidator<RecurrenceDto>
{
    /// <summary>A schedule may span at most ten years, which bounds any generation work.</summary>
    public const int MaxSpanDays = 3660;

    public RecurrenceDtoValidator()
    {
        RuleFor(x => x.Frequency).IsInEnum();
        RuleFor(x => x.Interval).InclusiveBetween(1, RecurrencePattern.MaxInterval);
        RuleFor(x => x.Days).Must(d => ((int)d & ~0x7F) == 0).WithMessage("Unknown weekday flags.");
        RuleFor(x => x.StartDate).ReasonableDate();
        RuleFor(x => x.EndDate).ReasonableDate()
            .Must((x, end) => end is null || (end >= x.StartDate && end.Value.DayNumber - x.StartDate.DayNumber <= MaxSpanDays))
            .WithMessage("End date must be on or after the start date and within ten years of it.");
    }
}
