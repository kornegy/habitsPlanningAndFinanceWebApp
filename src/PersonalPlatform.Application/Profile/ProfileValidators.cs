using FluentValidation;
using PersonalPlatform.Contracts.Profile;
using PersonalPlatform.Domain.Finance;
using PersonalPlatform.Domain.Users;

namespace PersonalPlatform.Application.Profile;

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).MaximumLength(UserProfile.DisplayNameMaxLength);
        RuleFor(x => x.TimeZoneId).NotEmpty().Must(TimeZones.IsSupported).WithMessage("Unknown time zone.");
        RuleFor(x => x.Locale).NotEmpty().Must(l => UserProfile.SupportedLocales.Contains(l)).WithMessage("Unsupported locale.");
        RuleFor(x => x.CurrencyCode).NotEmpty().Must(Currencies.IsSupported).WithMessage("Unsupported currency.");
        RuleFor(x => x.DateFormat).IsInEnum();
        RuleFor(x => x.Theme).IsInEnum();
        RuleFor(x => x.FirstDayOfWeek).Must(d => d is DayOfWeek.Monday or DayOfWeek.Sunday).WithMessage("Week must start on Monday or Sunday.");
    }
}
