using CsCheck;
using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Scheduling;
using PersonalPlatform.Domain.Tasks;

namespace PersonalPlatform.UnitTests.Domain;

public sealed class RecurrencePatternTests
{
    private static readonly DateOnly Start = new(2026, 1, 31); // a Saturday at the end of a long month

    [Fact]
    public void Daily_with_interval_skips_days()
    {
        var pattern = new RecurrencePattern(RecurrenceFrequency.Daily, 3, Weekdays.None, new DateOnly(2026, 3, 1), null);

        Assert.Equal([new(2026, 3, 1), new(2026, 3, 4), new(2026, 3, 7)], pattern.Between(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 8), 10));
        Assert.Equal(new DateOnly(2026, 3, 4), pattern.NextAfter(new DateOnly(2026, 3, 1)));
        Assert.Equal(new DateOnly(2026, 3, 4), pattern.FirstOnOrAfter(new DateOnly(2026, 3, 2)));
    }

    [Fact]
    public void Monthly_on_the_31st_is_clamped_to_shorter_months()
    {
        var pattern = new RecurrencePattern(RecurrenceFrequency.Monthly, 1, Weekdays.None, Start, null);

        var dates = pattern.Between(Start, new DateOnly(2026, 6, 30), 10);

        Assert.Equal([new(2026, 1, 31), new(2026, 2, 28), new(2026, 3, 31), new(2026, 4, 30), new(2026, 5, 31), new(2026, 6, 30)], dates);
    }

    [Fact]
    public void Monthly_in_a_leap_year_uses_february_29()
    {
        var pattern = new RecurrencePattern(RecurrenceFrequency.Monthly, 1, Weekdays.None, new DateOnly(2028, 1, 30), null);

        Assert.Equal(new DateOnly(2028, 2, 29), pattern.NextAfter(new DateOnly(2028, 1, 30)));
    }

    [Fact]
    public void Yearly_on_february_29_falls_back_to_february_28_in_common_years()
    {
        var pattern = new RecurrencePattern(RecurrenceFrequency.Yearly, 1, Weekdays.None, new DateOnly(2028, 2, 29), null);

        Assert.Equal([new(2028, 2, 29), new(2029, 2, 28), new(2030, 2, 28), new(2031, 2, 28), new(2032, 2, 29)],
            pattern.Between(new DateOnly(2028, 1, 1), new DateOnly(2032, 12, 31), 10));
    }

    [Fact]
    public void Weekly_on_selected_days_every_second_week()
    {
        // 2026-03-02 is a Monday.
        var pattern = new RecurrencePattern(RecurrenceFrequency.Weekly, 2, Weekdays.Monday | Weekdays.Thursday, new DateOnly(2026, 3, 2), null);

        Assert.Equal([new(2026, 3, 2), new(2026, 3, 5), new(2026, 3, 16), new(2026, 3, 19)],
            pattern.Between(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 22), 10));
    }

    [Fact]
    public void Weekly_without_days_uses_the_weekday_of_the_start_date()
    {
        var pattern = new RecurrencePattern(RecurrenceFrequency.Weekly, 1, Weekdays.None, Start, null);

        Assert.Equal(Weekdays.Saturday, pattern.Days);
        Assert.Equal(Start.AddDays(7), pattern.NextAfter(Start));
    }

    [Fact]
    public void End_date_stops_the_series()
    {
        var pattern = new RecurrencePattern(RecurrenceFrequency.Daily, 1, Weekdays.None, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3));

        Assert.Null(pattern.NextAfter(new DateOnly(2026, 1, 3)));
        Assert.False(pattern.OccursOn(new DateOnly(2026, 1, 4)));
        Assert.False(pattern.OccursOn(new DateOnly(2025, 12, 31)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Interval_out_of_range_is_rejected(int interval) =>
        Assert.Throws<DomainRuleException>(() => new RecurrencePattern(RecurrenceFrequency.Daily, interval, Weekdays.None, Start, null));

    [Fact]
    public void End_before_start_is_rejected() =>
        Assert.Throws<DomainRuleException>(() => new RecurrencePattern(RecurrenceFrequency.Daily, 1, Weekdays.None, Start, Start.AddDays(-1)));

    [Fact]
    public void Property_occurrences_are_strictly_increasing_valid_and_complete()
    {
        var gen = Gen.Select(
            Gen.Int[0, 3],
            Gen.Int[1, 40],
            Gen.Int[0, 127],
            Gen.Int[0, 3000],
            Gen.Int[0, 400]);

        gen.Sample((frequency, interval, days, startOffset, span) =>
        {
            var start = new DateOnly(2020, 1, 1).AddDays(startOffset);
            var pattern = new RecurrencePattern((RecurrenceFrequency)frequency, interval, (Weekdays)days, start, null);
            var to = start.AddDays(span);
            var occurrences = pattern.Between(start, to, 1000);

            // Every reported date is an occurrence, dates strictly increase, and no occurrence in the range is missed.
            Assert.All(occurrences, d => Assert.True(pattern.OccursOn(d)));
            Assert.True(occurrences.Zip(occurrences.Skip(1)).All(p => p.First < p.Second));
            for (var d = start; d <= to; d = d.AddDays(1))
            {
                Assert.Equal(pattern.OccursOn(d), occurrences.Contains(d));
            }
        }, iter: 300);
    }

    [Fact]
    public void Series_skips_missed_occurrences_when_generating_the_next_instance()
    {
        var series = new TaskSeries(Guid.NewGuid(), DateTimeOffset.UtcNow);
        series.SetTemplate("Water plants", null, TaskPriority.None, null, null,
            new RecurrencePattern(RecurrenceFrequency.Daily, 1, Weekdays.None, new DateOnly(2026, 1, 1), null), DateTimeOffset.UtcNow);

        // Completing an instance from Jan 1 on Jan 10 does not back-fill Jan 2..9.
        Assert.Equal(new DateOnly(2026, 1, 10), series.NextOccurrenceAfter(new DateOnly(2026, 1, 1), today: new DateOnly(2026, 1, 10)));
        Assert.Equal(new DateOnly(2026, 1, 2), series.NextOccurrenceAfter(new DateOnly(2026, 1, 1), today: new DateOnly(2026, 1, 1)));

        series.Stop(DateTimeOffset.UtcNow);
        Assert.Null(series.NextOccurrenceAfter(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Series_instance_is_independent_and_must_be_an_occurrence()
    {
        var series = new TaskSeries(Guid.NewGuid(), DateTimeOffset.UtcNow);
        series.SetTemplate("Report", "desc", TaskPriority.High, null, new TimeOnly(9, 0),
            new RecurrencePattern(RecurrenceFrequency.Weekly, 1, Weekdays.Monday, new DateOnly(2026, 3, 2), null), DateTimeOffset.UtcNow);

        var instance = series.CreateInstance(new DateOnly(2026, 3, 9), DateTimeOffset.UtcNow);

        Assert.Equal(series.Id, instance.SeriesId);
        Assert.Equal(new DateOnly(2026, 3, 9), instance.DueDate);
        Assert.Equal(new TimeOnly(9, 0), instance.DueTime);
        Assert.Throws<DomainRuleException>(() => series.CreateInstance(new DateOnly(2026, 3, 10), DateTimeOffset.UtcNow));
    }
}
