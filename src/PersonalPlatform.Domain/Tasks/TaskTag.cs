using PersonalPlatform.Domain.Common;

namespace PersonalPlatform.Domain.Tasks;

/// <summary>Join row between a task and a tag. Carries the owner for composite foreign keys.</summary>
public sealed class TaskTag : IOwnedEntity
{
    private TaskTag()
    {
    }

    internal TaskTag(Guid ownerId, Guid taskId, Guid tagId)
    {
        OwnerId = ownerId;
        TaskId = taskId;
        TagId = tagId;
    }

    public Guid OwnerId { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid TagId { get; private set; }
}
