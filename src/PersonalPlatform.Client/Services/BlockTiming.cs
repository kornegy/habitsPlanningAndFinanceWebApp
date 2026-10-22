namespace PersonalPlatform.Client.Services;

/// <summary>How a time block entered as a date plus start and end times of day maps onto the calendar.</summary>
/// <param name="EndDate">Local date on which the block ends.</param>
/// <param name="Duration">Length of the block.</param>
/// <param name="EndsAtMidnight">The end time is 00:00, i.e. the block runs to the end of its start day.</param>
/// <param name="EndsNextDay">The block ends after midnight, on the following day.</param>
public sealed record BlockTiming(DateOnly EndDate, TimeSpan Duration, bool EndsAtMidnight, bool EndsNextDay)
{
    /// <summary>An end time not later than the start means "after midnight": 22:00–00:00 runs to midnight and
    /// 22:00–02:00 ends the next day. Equal times describe a full day.</summary>
    public static BlockTiming Of(DateOnly date, TimeOnly start, TimeOnly end)
    {
        var overnight = end <= start;
        var duration = overnight ? TimeSpan.FromDays(1) - (start - end) : end - start;
        var midnight = end == TimeOnly.MinValue;
        return new BlockTiming(overnight ? date.AddDays(1) : date, duration, midnight, overnight && !midnight);
    }
}
