using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalPlatform.Contracts.Admin;
using PersonalPlatform.Infrastructure.Identity;

namespace PersonalPlatform.Api.Controllers;

/// <summary>Operator endpoints. Require the admin role, which can only be granted by deployment configuration.</summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminController(AdminStatsService stats) : ControllerBase
{
    [HttpGet("stats")]
    public Task<AdminStatsDto> Stats(CancellationToken cancellationToken) => stats.GetAsync(cancellationToken);
}
