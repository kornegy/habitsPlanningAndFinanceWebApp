using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Organization;

namespace PersonalPlatform.Domain.Tasks;

public enum TaskItemStatus
{
    Open = 0,
    InProgress = 1,
    Done = 2,
}

public enum TaskPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>
/// A to-do item. Instances generated from a <see cref="TaskSeries"/> are independent rows: editing one
/// instance never changes the series or other instances.
/// </summary>
public sealed class TaskItem : IOwnedEntity
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int MaxSubtasks = 50;
    public const int MaxTags = 10;

    private readonly List<Subtask> _subtasks = [];
    private readonly List<TaskTag> _tags = [];

    private TaskItem()
    {
    }

    public TaskItem(Guid ownerId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public TaskItemStatus Status { get; private set; }

    public TaskPriority Priority { get; private set; }

    /// <summary>Local calendar due date in the owner's time zone.</summary>
    public DateOnly? DueDate { get; private set; }

    public TimeOnly? DueTime { get; private set; }

    /// <summary>Absolute reminder instant (UTC). Shown in-app until dismissed.</summary>
    public DateTimeOffset? ReminderAt { get; private set; }

    public DateTimeOffset? ReminderDismissedAt { get; private set; }

    public Guid? CategoryId { get; private set; }

    public Guid? SeriesId { get; private set; }

    /// <summary>Scheduled date this instance was generated for (series instances only).</summary>
    public DateOnly? OccurrenceDate { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<Subtask> Subtasks => _subtasks;

    public IReadOnlyList<TaskTag> Tags => _tags;

    public bool IsDone => Status == TaskItemStatus.Done;

    public void SetDetails(string title, string? description, TaskPriority priority, Guid? categoryId, DateTimeOffset now)
    {
        Rule.Require(Enum.IsDefined(priority), "invalid_priority", "Unknown priority.");
        Title = Rule.RequireText(title, TitleMaxLength, "invalid_title", "Title");
        Description = Rule.OptionalText(description, DescriptionMaxLength, "invalid_description", "Description");
        Priority = priority;
        CategoryId = categoryId;
        UpdatedAt = now;
    }

    public void Schedule(DateOnly? dueDate, TimeOnly? dueTime, DateTimeOffset? reminderAt, DateTimeOffset now)
    {
        Rule.Require(dueTime is null || dueDate is not null, "due_time_without_date", "A due time requires a due date.");
        DueDate = dueDate;
        DueTime = dueTime;
        if (reminderAt != ReminderAt)
        {
            ReminderAt = reminderAt;
            ReminderDismissedAt = null;
        }

        UpdatedAt = now;
    }

    public void SetStatus(TaskItemStatus status, DateTimeOffset now)
    {
        Rule.Require(Enum.IsDefined(status), "invalid_status", "Unknown status.");
        if (status == Status)
        {
            return;
        }

        Status = status;
        CompletedAt = status == TaskItemStatus.Done ? now : null;
        UpdatedAt = now;
    }

    public void DismissReminder(DateTimeOffset now)
    {
        ReminderDismissedAt = now;
        UpdatedAt = now;
    }

    public void LinkToSeries(Guid seriesId, DateOnly occurrenceDate)
    {
        SeriesId = seriesId;
        OccurrenceDate = occurrenceDate;
    }

    public void ReplaceSubtasks(IEnumerable<(string Title, bool IsDone)> subtasks)
    {
        var list = subtasks.ToList();
        Rule.Require(list.Count <= MaxSubtasks, "too_many_subtasks", $"A task can have at most {MaxSubtasks} subtasks.");
        _subtasks.Clear();
        for (var i = 0; i < list.Count; i++)
        {
            _subtasks.Add(new Subtask(this, list[i].Title, list[i].IsDone, i));
        }
    }

    public void SetSubtaskDone(Guid subtaskId, bool isDone, DateTimeOffset now)
    {
        var subtask = _subtasks.FirstOrDefault(s => s.Id == subtaskId)
            ?? throw new DomainRuleException("subtask_not_found", "Subtask not found.");
        subtask.SetDone(isDone);
        UpdatedAt = now;
    }

    public void ReplaceTags(IReadOnlyCollection<Tag> tags)
    {
        Rule.Require(tags.Count <= MaxTags, "too_many_tags", $"A task can have at most {MaxTags} tags.");
        Rule.Require(tags.All(t => t.OwnerId == OwnerId), "tag_not_found", "Tag not found.");
        _tags.RemoveAll(existing => tags.All(t => t.Id != existing.TagId));
        foreach (var tag in tags.Where(t => _tags.All(existing => existing.TagId != t.Id)))
        {
            _tags.Add(new TaskTag(OwnerId, Id, tag.Id));
        }
    }
}
