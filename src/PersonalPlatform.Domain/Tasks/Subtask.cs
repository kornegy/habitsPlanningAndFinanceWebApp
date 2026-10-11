using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Tasks;

/// <summary>Checklist item of a task. Lifetime is bound to its parent task.</summary>
public sealed class Subtask : IOwnedEntity
{
    public const int TitleMaxLength = 200;

    private Subtask()
    {
    }

    internal Subtask(TaskItem task, string title, bool isDone, int position)
    {
        Id = Guid.CreateVersion7();
        OwnerId = task.OwnerId;
        TaskId = task.Id;
        Title = Rule.RequireText(title, TitleMaxLength, "invalid_subtask", "Subtask title");
        IsDone = isDone;
        Position = position;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid TaskId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public bool IsDone { get; private set; }

    public int Position { get; private set; }

    internal void SetDone(bool isDone) => IsDone = isDone;
}
