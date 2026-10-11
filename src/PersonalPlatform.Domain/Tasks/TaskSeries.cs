using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Scheduling;

namespace PersonalPlatform.Domain.Tasks;

/// <summary>
/// Template of a recurring task.
/// </summary>
/// <remarks>
/// Generation rules:
/// <list type="number">
/// <item>Creating a series materialises one instance for the first occurrence on/after the start date.</item>
/// <item>Completing an instance materialises the next instance: the first occurrence strictly after the
/// completed occurrence that is also on/after the owner's today (missed dates are skipped, not back-filled).</item>
/// <item>Instances are independent rows. Editing or rescheduling one instance changes only that instance.</item>
/// <item>Editing the series changes the template for instances generated afterwards; existing instances keep
/// their own data.</item>
/// <item>A unique index on <c>(series_id, occurrence_date)</c> guarantees that the same occurrence is never
/// materialised twice, even when an instance is completed, reopened and completed again concurrently.</item>
/// </list>
/// </remarks>
public sealed class TaskSeries : IOwnedEntity
{
    private TaskSeries()
    {
    }

    public TaskSeries(Guid ownerId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        CreatedAt = now;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public TaskPriority Priority { get; private set; }

    public Guid? CategoryId { get; private set; }

    public TimeOnly? DueTime { get; private set; }

    public RecurrenceFrequency Frequency { get; private set; }

    public int Interval { get; private set; }

    public Weekdays Days { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public RecurrencePattern Pattern => new(Frequency, Interval, Days, StartDate, EndDate);

    public void SetTemplate(string title, string? description, TaskPriority priority, Guid? categoryId, TimeOnly? dueTime, RecurrencePattern pattern, DateTimeOffset now)
    {
        Rule.Require(Enum.IsDefined(priority), "invalid_priority", "Unknown priority.");
        Title = Rule.RequireText(title, TaskItem.TitleMaxLength, "invalid_title", "Title");
        Description = Rule.OptionalText(description, TaskItem.DescriptionMaxLength, "invalid_description", "Description");
        Priority = priority;
        CategoryId = categoryId;
        DueTime = dueTime;
        Frequency = pattern.Frequency;
        Interval = pattern.Interval;
        Days = pattern.Days;
        StartDate = pattern.StartDate;
        EndDate = pattern.EndDate;
        UpdatedAt = now;
    }

    public void Stop(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public DateOnly? FirstOccurrence() => Pattern.FirstOnOrAfter(StartDate);

    /// <summary>Next occurrence to materialise after <paramref name="completedOccurrence"/> was completed.</summary>
    public DateOnly? NextOccurrenceAfter(DateOnly completedOccurrence, DateOnly today)
    {
        if (!IsActive)
        {
            return null;
        }

        var next = Pattern.NextAfter(completedOccurrence);
        return next is { } date && date < today ? Pattern.FirstOnOrAfter(today) : next;
    }

    public TaskItem CreateInstance(DateOnly occurrenceDate, DateTimeOffset now)
    {
        Rule.Require(Pattern.OccursOn(occurrenceDate), "invalid_occurrence", "Date is not an occurrence of the series.");
        var task = new TaskItem(OwnerId, now);
        task.SetDetails(Title, Description, Priority, CategoryId, now);
        task.Schedule(occurrenceDate, DueTime, reminderAt: null, now);
        task.LinkToSeries(Id, occurrenceDate);
        return task;
    }
}
