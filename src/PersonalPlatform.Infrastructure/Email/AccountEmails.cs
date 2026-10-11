using Microsoft.Extensions.Options;
using PersonalPlatform.Infrastructure.Identity;

namespace PersonalPlatform.Infrastructure.Email;

/// <summary>
/// Account-related email texts. Links carry tokens in the URL fragment (<c>#...</c>), which browsers never
/// send to servers or in Referer headers, so tokens do not end up in proxy or access logs.
/// </summary>
public sealed class AccountEmails(IOptions<AccountEmailOptions> options)
{
    public EmailMessage Confirmation(string to, Guid userId, string token) => new(
        to,
        "Подтверждение адреса / Confirm your email",
        $"""
        Чтобы подтвердить адрес электронной почты, откройте ссылку:
        To confirm your email address, open this link:

        {Link("confirm-email", userId, token)}

        Если вы не регистрировались, просто проигнорируйте это письмо.
        If you did not sign up, you can ignore this message.
        """);

    public EmailMessage AccountAlreadyExists(string to) => new(
        to,
        "Попытка регистрации / Sign-up attempt",
        $"""
        Кто-то попытался зарегистрироваться с этим адресом, но аккаунт уже существует.
        Someone tried to sign up with this address, but an account already exists.

        Если это были вы и вы забыли пароль, восстановите его здесь / If it was you, reset your password here:
        {Base()}/forgot-password
        """);

    public EmailMessage PasswordReset(string to, Guid userId, string token) => new(
        to,
        "Восстановление пароля / Password reset",
        $"""
        Чтобы задать новый пароль, откройте ссылку (действует ограниченное время):
        To set a new password, open this link (valid for a limited time):

        {Link("reset-password", userId, token)}

        Если вы не запрашивали восстановление, проигнорируйте это письмо — пароль не изменится.
        If you did not request this, ignore this message; your password will not change.
        """);

    public EmailMessage PasswordChanged(string to) => new(
        to,
        "Пароль изменён / Password changed",
        """
        Пароль вашего аккаунта был изменён, все остальные сеансы завершены.
        Your account password was changed and all other sessions were signed out.

        Если это были не вы, немедленно восстановите доступ через «Забыли пароль».
        If this was not you, reset your password immediately using "Forgot password".
        """);

    private string Base() => options.Value.PublicBaseUrl.TrimEnd('/');

    private string Link(string page, Guid userId, string token) =>
        $"{Base()}/{page}#userId={userId}&token={Uri.EscapeDataString(token)}";
}
