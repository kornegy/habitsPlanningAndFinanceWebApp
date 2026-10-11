using C = PersonalPlatform.Contracts;
using D = PersonalPlatform.Domain;

namespace PersonalPlatform.Application.Common;

/// <summary>
/// Contract enums mirror domain enums value-for-value (verified by unit tests) so that mapping is a cast.
/// Keeping them separate prevents the public API from changing whenever the domain model does.
/// </summary>
internal static class EnumMapping
{
    public static D.Tasks.TaskPriority ToDomain(this C.Tasks.TaskPriority value) => (D.Tasks.TaskPriority)value;

    public static C.Tasks.TaskPriority ToContract(this D.Tasks.TaskPriority value) => (C.Tasks.TaskPriority)value;

    public static D.Tasks.TaskItemStatus ToDomain(this C.Tasks.TaskItemStatus value) => (D.Tasks.TaskItemStatus)value;

    public static C.Tasks.TaskItemStatus ToContract(this D.Tasks.TaskItemStatus value) => (C.Tasks.TaskItemStatus)value;

    public static D.Scheduling.RecurrenceFrequency ToDomain(this C.Tasks.RecurrenceFrequency value) => (D.Scheduling.RecurrenceFrequency)value;

    public static C.Tasks.RecurrenceFrequency ToContract(this D.Scheduling.RecurrenceFrequency value) => (C.Tasks.RecurrenceFrequency)value;

    public static D.Scheduling.Weekdays ToDomain(this C.Tasks.Weekdays value) => (D.Scheduling.Weekdays)value;

    public static C.Tasks.Weekdays ToContract(this D.Scheduling.Weekdays value) => (C.Tasks.Weekdays)value;

    public static D.Organization.CategoryScope ToDomain(this C.Organization.CategoryScope value) => (D.Organization.CategoryScope)value;

    public static C.Organization.CategoryScope ToContract(this D.Organization.CategoryScope value) => (C.Organization.CategoryScope)value;

    public static D.Habits.HabitScheduleType ToDomain(this C.Habits.HabitScheduleType value) => (D.Habits.HabitScheduleType)value;

    public static C.Habits.HabitScheduleType ToContract(this D.Habits.HabitScheduleType value) => (C.Habits.HabitScheduleType)value;

    public static D.Finance.AccountType ToDomain(this C.Finance.AccountType value) => (D.Finance.AccountType)value;

    public static C.Finance.AccountType ToContract(this D.Finance.AccountType value) => (C.Finance.AccountType)value;

    public static D.Finance.CategoryKind ToDomain(this C.Finance.CategoryKind value) => (D.Finance.CategoryKind)value;

    public static C.Finance.CategoryKind ToContract(this D.Finance.CategoryKind value) => (C.Finance.CategoryKind)value;

    public static D.Finance.TransactionKind ToDomain(this C.Finance.TransactionKind value) => (D.Finance.TransactionKind)value;

    public static C.Finance.TransactionKind ToContract(this D.Finance.TransactionKind value) => (C.Finance.TransactionKind)value;

    public static C.Finance.BudgetState ToContract(this D.Finance.BudgetState value) => (C.Finance.BudgetState)value;

    public static D.Scheduling.RecurrencePattern ToDomain(this C.Tasks.RecurrenceDto dto) =>
        new(dto.Frequency.ToDomain(), dto.Interval, dto.Days.ToDomain(), dto.StartDate, dto.EndDate);

    public static C.Tasks.RecurrenceDto ToContract(this D.Scheduling.RecurrencePattern pattern) =>
        new(pattern.Frequency.ToContract(), pattern.Interval, pattern.Days.ToContract(), pattern.StartDate, pattern.EndDate);
}
