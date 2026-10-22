using Microsoft.AspNetCore.Mvc;
using PersonalPlatform.Application.Habits;
using PersonalPlatform.Contracts.Habits;
using PersonalPlatform.Contracts.Notes;

namespace PersonalPlatform.Api.Controllers;

/// <summary>Habits the user is giving up: days without the habit, relapses and milestones.</summary>
[ApiController]
[Route("api/v1/quit-habits")]
public sealed class QuitHabitsController(QuitHabitService quitHabits) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<QuitHabitDto>> List([FromQuery] bool includeArchived, CancellationToken cancellationToken) =>
        quitHabits.ListAsync(includeArchived, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<QuitHabitDetailDto> Get(Guid id, CancellationToken cancellationToken) => quitHabits.GetAsync(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<QuitHabitDetailDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateQuitHabitRequest request, CancellationToken cancellationToken)
    {
        var habit = await quitHabits.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/quit-habits/{habit.Habit.Id}", habit);
    }

    [HttpPut("{id:guid}")]
    public Task<QuitHabitDetailDto> Update(Guid id, UpdateQuitHabitRequest request, CancellationToken cancellationToken) =>
        quitHabits.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/archive")]
    public Task<QuitHabitDetailDto> SetArchived(Guid id, SetArchivedRequest request, CancellationToken cancellationToken) =>
        quitHabits.SetArchivedAsync(id, request.IsArchived, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await quitHabits.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Records a relapse on a local date (idempotent per date).</summary>
    [HttpPost("{id:guid}/relapses")]
    public Task<QuitHabitDetailDto> LogRelapse(Guid id, LogRelapseRequest request, CancellationToken cancellationToken) =>
        quitHabits.LogRelapseAsync(id, request, cancellationToken);

    /// <summary>Removes the relapse recorded on a local date (idempotent).</summary>
    [HttpDelete("{id:guid}/relapses/{date}")]
    public Task<QuitHabitDetailDto> DeleteRelapse(Guid id, DateOnly date, CancellationToken cancellationToken) =>
        quitHabits.DeleteRelapseAsync(id, date, cancellationToken);
}
