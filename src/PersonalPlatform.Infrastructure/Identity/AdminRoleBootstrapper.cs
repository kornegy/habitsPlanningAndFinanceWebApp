using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalPlatform.Contracts.Admin;

namespace PersonalPlatform.Infrastructure.Identity;

/// <summary>
/// Grants the admin role to the confirmed accounts listed in <c>Bootstrap:AdminEmails</c> at startup.
/// There is deliberately no API to grant roles; administrators are designated by deployment configuration.
/// </summary>
public sealed partial class AdminRoleBootstrapper(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<AdminRoleBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var emails = configuration.GetSection("Bootstrap:AdminEmails").Get<string[]>() ?? [];
        if (emails.Length == 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        foreach (var email in emails)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is not { EmailConfirmed: true } || await users.IsInRoleAsync(user, Roles.Admin))
            {
                continue;
            }

            await users.AddToRoleAsync(user, Roles.Admin);
            LogGranted(logger, user.Id);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "security.admin_role_granted user={UserId} (bootstrap configuration)")]
    private static partial void LogGranted(ILogger logger, Guid userId);
}
