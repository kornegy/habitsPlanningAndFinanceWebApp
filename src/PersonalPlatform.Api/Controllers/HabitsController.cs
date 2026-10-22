using Microsoft.AspNetCore.Mvc;
using PersonalPlatform.Application.Habits;
using PersonalPlatform.Contracts.Habits;
using PersonalPlatform.Contracts.Notes;

namespace PersonalPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/habits")]
public sealed class HabitsController(HabitService habits) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<HabitDto>> List([FromQuery] bool includeArchived, CancellationToken cancellationToken) =>
        habits.ListAsync(includeArchived, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<HabitDto> Get(Guid id, CancellationToken cancellationToken) => habits.GetAsync(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<HabitDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateHabitRequest request, CancellationToken cancellationToken)
    {
        var habit = await habits.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/habits/{habit.Id}", habit);
    }

    [HttpPut("{id:guid}")]
    public Task<HabitDto> Update(Guid id, UpdateHabitRequest request, CancellationToken cancellationToken) =>
        habits.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/archive")]
    public Task<HabitDto> SetArchived(Guid id, SetArchivedRequest request, CancellationToken cancellationToken) =>
        habits.SetArchivedAsync(id, request.IsArchived, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await habits.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Removes the check-offs of all active habits and restarts them from today.</summary>
    [HttpPost("reset")]
    public Task<HabitResetResult> ResetAll(CancellationToken cancellationToken) => habits.ResetAsync(null, cancellationToken);

    /// <summary>Removes the check-offs of one habit and restarts it from today.</summary>
    [HttpPost("{id:guid}/reset")]
    public Task<HabitResetResult> Reset(Guid id, CancellationToken cancellationToken) => habits.ResetAsync(id, cancellationToken);

    /// <summary>Checks the habit off for a local date (idempotent).</summary>
    [HttpPut("{id:guid}/completions/{date}")]
    public Task<HabitCompletionResult> Complete(Guid id, DateOnly date, CancellationToken cancellationToken) =>
        habits.CompleteAsync(id, date, cancellationToken);

    /// <summary>Removes the check-off for a local date (idempotent).</summary>
    [HttpDelete("{id:guid}/completions/{date}")]
    public Task<HabitCompletionResult> Uncomplete(Guid id, DateOnly date, CancellationToken cancellationToken) =>
        habits.UncompleteAsync(id, date, cancellationToken);

    [HttpGet("{id:guid}/history")]
    public Task<HabitHistoryDto> History(Guid id, [FromQuery] HabitRangeQuery range, CancellationToken cancellationToken) =>
        habits.HistoryAsync(id, range.From, range.To, cancellationToken);

    [HttpGet("{id:guid}/stats")]
    public Task<HabitStatsDto> Stats(Guid id, [FromQuery] HabitRangeQuery range, CancellationToken cancellationToken) =>
        habits.StatsAsync(id, range.From, range.To, cancellationToken);
}
