namespace PersonalPlatform.Domain.Habits;

/// <param name="CompletionRate">Completed / scheduled * 100 for the period, one decimal place; 0 when nothing was scheduled.</param>
public sealed record HabitStats(int CurrentStreak, int LongestStreak, int ScheduledCount, int CompletedCount, decimal CompletionRate);

/// <summary>
/// Computes habit statistics from stored completions only (never from client-provided numbers).
/// </summary>
/// <remarks>
/// Streak semantics: a streak is a run of consecutive <em>scheduled</em> dates that were all completed.
/// Non-scheduled dates neither break nor extend a streak. Today does not break the current streak while it
/// is still in progress (the user may complete it later today). Completions on non-scheduled dates
/// (possible only if the schedule was edited later) are ignored.
/// </remarks>
public static class HabitStatistics
{
    /// <summary>Longest period scanned for streaks, keeping the computation bounded.</summary>
    public const int MaxHistoryDays = 3660;

    public static HabitStats Calculate(HabitSchedule schedule, DateOnly startDate, IReadOnlySet<DateOnly> completions, DateOnly today, DateOnly periodFrom, DateOnly periodTo)
    {
        var historyStart = startDate;
        if (today.DayNumber - historyStart.DayNumber > MaxHistoryDays)
        {
            historyStart = today.AddDays(-MaxHistoryDays);
        }

        var current = CurrentStreak(schedule, startDate, historyStart, completions, today);
        var longest = LongestStreak(schedule, startDate, historyStart, completions, today);
        var (scheduled, completed) = CountPeriod(schedule, startDate, completions, today, periodFrom, periodTo);
        var rate = scheduled == 0 ? 0m : decimal.Round(completed * 100m / scheduled, 1, MidpointRounding.AwayFromZero);
        return new HabitStats(current, Math.Max(longest, current), scheduled, completed, rate);
    }

    public static int CurrentStreak(HabitSchedule schedule, DateOnly startDate, DateOnly historyStart, IReadOnlySet<DateOnly> completions, DateOnly today)
    {
        var streak = 0;
        for (var date = today; date >= historyStart; date = date.AddDays(-1))
        {
            if (!schedule.IsDue(date, startDate))
            {
                continue;
            }

            if (completions.Contains(date))
            {
                streak++;
            }
            else if (date != today)
            {
                break;
            }
        }

        return streak;
    }

    public static int LongestStreak(HabitSchedule schedule, DateOnly startDate, DateOnly historyStart, IReadOnlySet<DateOnly> completions, DateOnly today)
    {
        int longest = 0, run = 0;
        for (var date = historyStart; date <= today; date = date.AddDays(1))
        {
            if (!schedule.IsDue(date, startDate))
            {
                continue;
            }

            if (completions.Contains(date))
            {
                run++;
                longest = Math.Max(longest, run);
            }
            else if (date != today)
            {
                run = 0;
            }
        }

        return longest;
    }

    /// <summary>
    /// Scheduled and completed counts within the period, clipped to [start date, today]. Today is counted as
    /// scheduled only once it is completed, so an unfinished today does not lower the rate.
    /// </summary>
    public static (int Scheduled, int Completed) CountPeriod(HabitSchedule schedule, DateOnly startDate, IReadOnlySet<DateOnly> completions, DateOnly today, DateOnly periodFrom, DateOnly periodTo)
    {
        var earliest = today.AddDays(-MaxHistoryDays);
        var from = periodFrom < startDate ? startDate : periodFrom;
        from = from < earliest ? earliest : from;
        var to = periodTo > today ? today : periodTo;
        int scheduled = 0, completed = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (!schedule.IsDue(date, startDate))
            {
                continue;
            }

            var done = completions.Contains(date);
            if (date == today && !done)
            {
                continue;
            }

            scheduled++;
            if (done)
            {
                completed++;
            }
        }

        return (scheduled, completed);
    }
}
