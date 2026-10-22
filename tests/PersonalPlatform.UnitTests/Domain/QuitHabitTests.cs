using CsCheck;
using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Habits;

namespace PersonalPlatform.UnitTests.Domain;

public sealed class QuitHabitTests
{
    private static readonly DateOnly QuitDate = new(2026, 3, 1);
    private static readonly DateTimeOffset Now = new(2026, 3, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Without_relapses_progress_is_the_time_since_the_quit_date()
    {
        var progress = QuitHabitStatistics.Calculate(QuitDate, [], QuitDate.AddDays(10));

        Assert.Equal(new QuitProgress(10, 10, 0, null, 14), progress);
    }

    [Fact]
    public void A_relapse_restarts_the_counter_but_keeps_the_longest_run()
    {
        var relapses = new[] { QuitDate.AddDays(12), QuitDate.AddDays(15) };

        var progress = QuitHabitStatistics.Calculate(QuitDate, relapses, QuitDate.AddDays(20));

        Assert.Equal(5, progress.CurrentStreakDays);
        Assert.Equal(12, progress.LongestStreakDays);
        Assert.Equal(2, progress.RelapseCount);
        Assert.Equal(QuitDate.AddDays(15), progress.LastRelapse);
        Assert.Equal(7, progress.NextMilestoneDays);
    }

    [Fact]
    public void Relapses_outside_the_tracked_period_and_duplicates_are_ignored()
    {
        var relapses = new[] { QuitDate.AddDays(-3), QuitDate.AddDays(4), QuitDate.AddDays(4), QuitDate.AddDays(30) };

        var progress = QuitHabitStatistics.Calculate(QuitDate, relapses, QuitDate.AddDays(6));

        Assert.Equal(1, progress.RelapseCount);
        Assert.Equal(2, progress.CurrentStreakDays);
        Assert.Equal(4, progress.LongestStreakDays);
    }

    [Fact]
    public void Relapse_today_resets_to_zero_and_past_the_last_milestone_there_is_no_next_goal()
    {
        Assert.Equal(0, QuitHabitStatistics.Calculate(QuitDate, [QuitDate.AddDays(3)], QuitDate.AddDays(3)).CurrentStreakDays);
        Assert.Equal(1, QuitHabitStatistics.Calculate(QuitDate, [], QuitDate).NextMilestoneDays);
        Assert.Null(QuitHabitStatistics.Calculate(QuitDate, [], QuitDate.AddDays(1095)).NextMilestoneDays);
    }

    [Fact]
    public void Property_streaks_are_bounded_by_the_tracked_period()
    {
        Gen.Select(Gen.Int[0, 400], Gen.Int[0, 400].Array[0, 20]).Sample((days, offsets) =>
        {
            var today = QuitDate.AddDays(days);
            var progress = QuitHabitStatistics.Calculate(QuitDate, offsets.Select(o => QuitDate.AddDays(o)), today);

            Assert.InRange(progress.CurrentStreakDays, 0, days);
            Assert.InRange(progress.LongestStreakDays, progress.CurrentStreakDays, days);
            Assert.True(progress.NextMilestoneDays is null || progress.NextMilestoneDays > progress.CurrentStreakDays);
        }, iter: 500);
    }

    [Fact]
    public void Relapses_must_fall_between_the_quit_date_and_today_on_an_active_habit()
    {
        var today = QuitDate.AddDays(10);
        var habit = new QuitHabit(Guid.NewGuid(), Now);
        habit.Update("  Smoking ", null, null, QuitDate, today, Now);

        Assert.Equal("Smoking", habit.Name);
        Assert.Equal("date_in_future", Code(() => habit.Relapse(today.AddDays(1), null, today, Now)));
        Assert.Equal("date_before_quit", Code(() => habit.Relapse(QuitDate.AddDays(-1), null, today, Now)));
        Assert.Equal("invalid_note", Code(() => habit.Relapse(today, new string('n', QuitHabitRelapse.NoteMaxLength + 1), today, Now)));
        Assert.Equal(habit.OwnerId, habit.Relapse(today, "party", today, Now).OwnerId);

        habit.Archive(Now);
        Assert.Equal("habit_archived", Code(() => habit.Relapse(today, null, today, Now)));
        Assert.Equal("date_in_future", Code(() => habit.Update("Smoking", null, null, today.AddDays(1), today, Now)));
    }

    [Fact]
    public void Habit_restart_moves_a_past_start_date_to_today_and_keeps_a_future_one()
    {
        var today = new DateOnly(2026, 5, 10);
        var habit = new Habit(Guid.NewGuid(), today.AddDays(-30), Now);
        habit.Restart(today, Now);
        Assert.Equal(today, habit.StartDate);
        Assert.Equal("date_before_start", Code(() => habit.Complete(today.AddDays(-1), today, Now)));

        var future = new Habit(Guid.NewGuid(), today.AddDays(5), Now);
        future.Restart(today, Now);
        Assert.Equal(today.AddDays(5), future.StartDate);
        Assert.Equal(HabitScheduleType.Daily, future.Schedule.Type);
    }

    private static string Code(Action action) => Assert.Throws<DomainRuleException>(action).Code;
}
