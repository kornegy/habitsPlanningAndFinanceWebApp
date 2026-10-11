using FluentValidation;
using PersonalPlatform.Contracts.Auth;
using PersonalPlatform.Domain.Users;

namespace PersonalPlatform.Application.Auth;

internal static class AuthRules
{
    public const int TokenMaxLength = 2048;

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(EmailPolicy.MaxLength).EmailAddress();

    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(PasswordPolicy.MinLength).MaximumLength(PasswordPolicy.MaxLength);
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).NewPassword();
        RuleFor(x => x.DisplayName).MaximumLength(UserProfile.DisplayNameMaxLength);
        RuleFor(x => x.TimeZoneId).MaximumLength(TimeZones.MaxIdLength);
        RuleFor(x => x.Locale).MaximumLength(10);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // No strength rules on login: they would only help attackers learn the policy per account.
        RuleFor(x => x.Email).NotEmpty().MaximumLength(EmailPolicy.MaxLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
    }
}

public sealed class EmailRequestValidator : AbstractValidator<EmailRequest>
{
    public EmailRequestValidator() => RuleFor(x => x.Email).ValidEmail();
}

public sealed class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(AuthRules.TokenMaxLength);
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(AuthRules.TokenMaxLength);
        RuleFor(x => x.NewPassword).NewPassword();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
        RuleFor(x => x.NewPassword).NewPassword().NotEqual(x => x.CurrentPassword).WithMessage("The new password must differ from the current one.");
    }
}

public sealed class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    public DeleteAccountRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
        RuleFor(x => x.Confirmation).Equal(DeleteAccountRequest.RequiredConfirmation).WithMessage("Type DELETE to confirm.");
    }
}
