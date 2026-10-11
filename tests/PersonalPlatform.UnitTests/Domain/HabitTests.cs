using CsCheck;
using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Habits;
using PersonalPlatform.Domain.Scheduling;

namespace PersonalPlatform.UnitTests.Domain;

public sealed class HabitTests
{
    private static readonly DateOnly Start = new(2026, 3, 2); // Monday
    private static readonly HabitSchedule Daily = new(HabitScheduleType.Daily, Weekdays.None, 1);

    [Fact]
    public void Weekday_schedule_is_due_only_on_selected_days()
    {
        var schedule = new HabitSchedule(HabitScheduleType.SpecificWeekdays, Weekdays.Monday | Weekdays.Wednesday, 1);

        Assert.True(schedule.IsDue(new DateOnly(2026, 3, 4), Start));
        Assert.False(schedule.IsDue(new DateOnly(2026, 3, 5), Start));
        Assert.False(schedule.IsDue(new DateOnly(2026, 3, 1), Start)); // before start
    }

    [Fact]
    public void Every_n_days_counts_from_the_start_date()
    {
        var schedule = new HabitSchedule(HabitScheduleType.EveryNDays, Weekdays.None, 3);

        Assert.True(schedule.IsDue(Start.AddDays(6), Start));
        Assert.False(schedule.IsDue(Start.AddDays(7), Start));
    }

    [Theory]
    [InlineData(HabitScheduleType.SpecificWeekdays, 0, 1)]
    [InlineData(HabitScheduleType.EveryNDays, 0, 1)]
    [InlineData(HabitScheduleType.EveryNDays, 0, 366)]
    public void Invalid_schedules_are_rejected(HabitScheduleType type, int days, int interval) =>
        Assert.Throws<DomainRuleException>(() => new HabitSchedule(type, (Weekdays)days, interval));

    [Fact]
    public void Current_streak_is_not_broken_by_an_unfinished_today()
    {
        var today = Start.AddDays(5);
        var completions = new HashSet<DateOnly> { Start.AddDays(2), Start.AddDays(3), Start.AddDays(4) };

        var stats = HabitStatistics.Calculate(Daily, Start, completions, today, Start, today);

        Assert.Equal(3, stats.CurrentStreak);
        Assert.Equal(3, stats.LongestStreak);
        Assert.Equal(5, stats.ScheduledCount); // today excluded until completed
        Assert.Equal(3, stats.CompletedCount);
        Assert.Equal(60.0m, stats.CompletionRate);
    }

    [Fact]
    public void A_missed_scheduled_day_breaks_the_streak_but_unscheduled_days_do_not()
    {
        var schedule = new HabitSchedule(HabitScheduleType.SpecificWeekdays, Weekdays.Monday | Weekdays.Friday, 1);
        var completions = new HashSet<DateOnly> { new(2026, 3, 2), new(2026, 3, 6), new(2026, 3, 9) }; // Mon, Fri, Mon
        var today = new DateOnly(2026, 3, 11); // Wednesday

        Assert.Equal(3, HabitStatistics.CurrentStreak(schedule, Start, Start, completions, today));

        completions.Remove(new DateOnly(2026, 3, 6));
        Assert.Equal(1, HabitStatistics.CurrentStreak(schedule, Start, Start, completions, today));
        Assert.Equal(1, HabitStatistics.LongestStreak(schedule, Start, Start, completions, today));
    }

    [Fact]
    public void Completion_rules_reject_future_unscheduled_pre_start_and_archived_dates()
    {
        var habit = new Habit(Guid.NewGuid(), Start, DateTimeOffset.UtcNow);
        habit.Update("Read", null, null, null, new HabitSchedule(HabitScheduleType.SpecificWeekdays, Weekdays.Monday, 1), Start, null, null, DateTimeOffset.UtcNow);
        var today = new DateOnly(2026, 3, 16);

        Assert.Equal("date_in_future", Assert.Throws<DomainRuleException>(() => habit.Complete(today.AddDays(7), today, DateTimeOffset.UtcNow)).Code);
        Assert.Equal("date_not_scheduled", Assert.Throws<DomainRuleException>(() => habit.Complete(today.AddDays(-1), today, DateTimeOffset.UtcNow)).Code);
        Assert.Equal("date_before_start", Assert.Throws<DomainRuleException>(() => habit.Complete(Start.AddDays(-7), today, DateTimeOffset.UtcNow)).Code);
        Assert.Equal(habit.Id, habit.Complete(today, today, DateTimeOffset.UtcNow).HabitId);

        habit.Archive(DateTimeOffset.UtcNow);
        Assert.Equal("habit_archived", Assert.Throws<DomainRuleException>(() => habit.Complete(today, today, DateTimeOffset.UtcNow)).Code);
    }

    [Fact]
    public void Property_statistics_invariants_hold_for_any_history()
    {
        var gen = Gen.Select(Gen.Int[0, 2], Gen.Int[1, 127], Gen.Int[2, 10], Gen.Int[0, 120], Gen.Int[0, 119].HashSet[0, 120]);

        gen.Sample((type, days, interval, length, completedOffsets) =>
        {
            var schedule = new HabitSchedule((HabitScheduleType)type, (Weekdays)days, interval);
            var today = Start.AddDays(length);
            var completions = completedOffsets.Select(o => Start.AddDays(o)).Where(d => d <= today && schedule.IsDue(d, Start)).ToHashSet();

            var stats = HabitStatistics.Calculate(schedule, Start, completions, today, Start, today);

            Assert.InRange(stats.CompletedCount, 0, stats.ScheduledCount);
            Assert.InRange(stats.CompletionRate, 0m, 100m);
            Assert.True(stats.CurrentStreak <= stats.LongestStreak);
            Assert.True(stats.LongestStreak <= completions.Count);
        }, iter: 300);
    }
}
