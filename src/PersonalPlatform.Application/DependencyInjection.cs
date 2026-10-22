using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Application.Dashboard;
using PersonalPlatform.Application.Export;
using PersonalPlatform.Application.Finance;
using PersonalPlatform.Application.Habits;
using PersonalPlatform.Application.Notes;
using PersonalPlatform.Application.Organization;
using PersonalPlatform.Application.Planner;
using PersonalPlatform.Application.Profile;
using PersonalPlatform.Application.Tasks;

namespace PersonalPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<UserClock>();
        services.AddScoped<ProfileService>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<TaskService>();
        services.AddScoped<HabitService>();
        services.AddScoped<QuitHabitService>();
        services.AddScoped<PlannerService>();
        services.AddScoped<AccountService>();
        services.AddScoped<FinanceCategoryService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<BudgetService>();
        services.AddScoped<RecurringTransactionService>();
        services.AddScoped<FinanceReportService>();
        services.AddScoped<NoteService>();
        services.AddScoped<JournalService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ReminderService>();
        services.AddScoped<ExportService>();
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
