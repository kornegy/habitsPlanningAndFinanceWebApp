using Microsoft.AspNetCore.Mvc;
using PersonalPlatform.Application.Dashboard;
using PersonalPlatform.Application.Tasks;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Dashboard;
using PersonalPlatform.Contracts.Tasks;

namespace PersonalPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/tasks")]
public sealed class TasksController(TaskService tasks) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<TaskDto>> List([FromQuery] TaskQuery query, CancellationToken cancellationToken) =>
        tasks.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<TaskDto> Get(Guid id, CancellationToken cancellationToken) => tasks.GetAsync(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<TaskDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await tasks.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/tasks/{task.Id}", task);
    }

    [HttpPut("{id:guid}")]
    public Task<TaskDto> Update(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken) =>
        tasks.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/status")]
    public Task<TaskDto> SetStatus(Guid id, SetTaskStatusRequest request, CancellationToken cancellationToken) =>
        tasks.SetStatusAsync(id, request.Status, cancellationToken);

    [HttpPost("{id:guid}/reschedule")]
    public Task<TaskDto> Reschedule(Guid id, RescheduleTaskRequest request, CancellationToken cancellationToken) =>
        tasks.RescheduleAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/subtasks/{subtaskId:guid}/done")]
    public Task<TaskDto> SetSubtaskDone(Guid id, Guid subtaskId, SetSubtaskDoneRequest request, CancellationToken cancellationToken) =>
        tasks.SetSubtaskDoneAsync(id, subtaskId, request.IsDone, cancellationToken);

    [HttpPost("{id:guid}/dismiss-reminder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DismissReminder(Guid id, CancellationToken cancellationToken)
    {
        await tasks.DismissReminderAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await tasks.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Complete, reopen, reschedule or delete up to 100 tasks in one atomic operation.</summary>
    [HttpPost("bulk")]
    public Task<BulkTaskResult> Bulk(BulkTaskRequest request, CancellationToken cancellationToken) =>
        tasks.BulkAsync(request, cancellationToken);
}

[ApiController]
[Route("api/v1/task-series")]
public sealed class TaskSeriesController(TaskService tasks) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public Task<TaskSeriesDto> Get(Guid id, CancellationToken cancellationToken) => tasks.GetSeriesAsync(id, cancellationToken);

    [HttpPut("{id:guid}")]
    public Task<TaskSeriesDto> Update(Guid id, UpdateTaskSeriesRequest request, CancellationToken cancellationToken) =>
        tasks.UpdateSeriesAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/stop")]
    public Task<TaskSeriesDto> Stop(Guid id, CancellationToken cancellationToken) => tasks.StopSeriesAsync(id, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await tasks.DeleteSeriesAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/reminders")]
public sealed class RemindersController(ReminderService reminders) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ReminderDto>> Due(CancellationToken cancellationToken) => reminders.DueAsync(cancellationToken);
}
