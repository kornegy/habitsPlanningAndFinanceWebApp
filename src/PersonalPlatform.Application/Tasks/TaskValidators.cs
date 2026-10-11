using FluentValidation;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Tasks;
using PersonalPlatform.Domain.Tasks;

namespace PersonalPlatform.Application.Tasks;

public sealed class SubtaskInputValidator : AbstractValidator<SubtaskInput>
{
    public SubtaskInputValidator() => RuleFor(x => x.Title).NotEmpty().MaximumLength(Subtask.TitleMaxLength);
}

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(TaskItem.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.DueDate).ReasonableDate();
        RuleFor(x => x.DueTime).Null().When(x => x.DueDate is null).WithMessage("A due time requires a due date.");
        RuleFor(x => x.ReminderAt).ReasonableInstant();
        RuleFor(x => x.TagIds).DistinctIds(TaskItem.MaxTags);
        RuleFor(x => x.Subtasks).Must(s => s is null || s.Count <= TaskItem.MaxSubtasks).WithMessage($"At most {TaskItem.MaxSubtasks} subtasks.");
        RuleForEach(x => x.Subtasks).SetValidator(new SubtaskInputValidator());
        RuleFor(x => x.Recurrence!).SetValidator(new RecurrenceDtoValidator()).When(x => x.Recurrence is not null);
        RuleFor(x => x.DueDate).Null().When(x => x.Recurrence is not null)
            .WithMessage("Recurring tasks take their due dates from the schedule.");
    }
}

public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(TaskItem.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.DueDate).ReasonableDate();
        RuleFor(x => x.DueTime).Null().When(x => x.DueDate is null).WithMessage("A due time requires a due date.");
        RuleFor(x => x.ReminderAt).ReasonableInstant();
        RuleFor(x => x.TagIds).DistinctIds(TaskItem.MaxTags);
        RuleFor(x => x.Subtasks).Must(s => s is null || s.Count <= TaskItem.MaxSubtasks).WithMessage($"At most {TaskItem.MaxSubtasks} subtasks.");
        RuleForEach(x => x.Subtasks).SetValidator(new SubtaskInputValidator());
    }
}

public sealed class SetTaskStatusRequestValidator : AbstractValidator<SetTaskStatusRequest>
{
    public SetTaskStatusRequestValidator() => RuleFor(x => x.Status).IsInEnum();
}

public sealed class RescheduleTaskRequestValidator : AbstractValidator<RescheduleTaskRequest>
{
    public RescheduleTaskRequestValidator()
    {
        RuleFor(x => x.DueDate).ReasonableDate();
        RuleFor(x => x.DueTime).Null().When(x => x.DueDate is null).WithMessage("A due time requires a due date.");
    }
}

public sealed class BulkTaskRequestValidator : AbstractValidator<BulkTaskRequest>
{
    public BulkTaskRequestValidator()
    {
        RuleFor(x => x.Ids).NotEmpty();
        RuleFor(x => x.Ids).Must(ids => ids is null || ids.Count <= QueryLimits.MaxBulkItems)
            .WithMessage($"At most {QueryLimits.MaxBulkItems} tasks per bulk operation.");
        RuleFor(x => x.Ids).Must(ids => ids is null || !ids.Contains(Guid.Empty)).WithMessage("Ids must not be empty.");
        RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.DueDate).ReasonableDate();
        RuleFor(x => x.DueDate).Null().When(x => x.Action != BulkTaskAction.Reschedule)
            .WithMessage("A due date is only used by the reschedule action.");
    }
}

public sealed class TaskQueryValidator : AbstractValidator<TaskQuery>
{
    public TaskQueryValidator()
    {
        RuleFor(x => x.View).IsInEnum();
        RuleFor(x => x.Sort).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.Search).ValidSearch();
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        this.ValidRange(x => x.From, x => x.To, 3660);
    }
}

public sealed class UpdateTaskSeriesRequestValidator : AbstractValidator<UpdateTaskSeriesRequest>
{
    public UpdateTaskSeriesRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(TaskItem.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.Recurrence).NotNull().SetValidator(new RecurrenceDtoValidator());
    }
}
