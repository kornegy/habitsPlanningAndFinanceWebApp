using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Application.Profile;

namespace PersonalPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<UserClock>();
        services.AddScoped<ProfileService>();
        AddValidators(services);
        return services;
    }

    /// <summary>Registers every concrete <see cref="IValidator{T}"/> declared in this assembly.</summary>
    private static void AddValidators(IServiceCollection services)
    {
        var validators = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
                .Select(i => (Service: i, Implementation: t)));
        foreach (var (service, implementation) in validators)
        {
            services.AddScoped(service, implementation);
        }
    }
}
