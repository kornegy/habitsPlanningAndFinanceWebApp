using Microsoft.AspNetCore.Mvc;
using PersonalPlatform.Application.Planner;
using PersonalPlatform.Contracts.Planner;

namespace PersonalPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/planner")]
public sealed class PlannerController(PlannerService planner) : ControllerBase
{
    /// <summary>Blocks, due habits and due tasks for a range of local dates (at most 42 days).</summary>
    [HttpGet]
    public Task<PlannerRangeDto> Range([FromQuery] PlannerRangeQuery range, CancellationToken cancellationToken) =>
        planner.GetRangeAsync(range.From, range.To, cancellationToken);

    [HttpGet("blocks/{id:guid}")]
    public Task<TimeBlockDto> Get(Guid id, CancellationToken cancellationToken) => planner.GetBlockAsync(id, cancellationToken);

    [HttpPost("blocks")]
    [ProducesResponseType<TimeBlockSaveResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateTimeBlockRequest request, CancellationToken cancellationToken)
    {
        var result = await planner.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/planner/blocks/{result.Block.Id}", result);
    }

    [HttpPut("blocks/{id:guid}")]
    public Task<TimeBlockSaveResult> Update(Guid id, UpdateTimeBlockRequest request, CancellationToken cancellationToken) =>
        planner.UpdateAsync(id, request, cancellationToken);

    [HttpPost("blocks/{id:guid}/move")]
    public Task<TimeBlockSaveResult> Move(Guid id, MoveTimeBlockRequest request, CancellationToken cancellationToken) =>
        planner.MoveAsync(id, request, cancellationToken);

    [HttpDelete("blocks/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await planner.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
