using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalPlatform.Api.Infrastructure;
using PersonalPlatform.Application.Dashboard;
using PersonalPlatform.Application.Export;
using PersonalPlatform.Contracts.Dashboard;

namespace PersonalPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public Task<DashboardDto> Get(CancellationToken cancellationToken) => dashboard.GetAsync(cancellationToken);
}

/// <summary>Downloads of the caller's own data. Strictly rate limited; bounded ranges and sizes.</summary>
[ApiController]
[Route("api/v1/export")]
[EnableRateLimiting(RateLimiting.Export)]
public sealed class ExportController(ExportService exports) : ControllerBase
{
    [HttpGet("transactions.csv")]
    public async Task<IActionResult> Transactions([FromQuery] ExportRangeQuery range, CancellationToken cancellationToken)
    {
        var content = await exports.TransactionsCsvAsync(range.From, range.To, cancellationToken);
        return File(content, "text/csv; charset=utf-8", $"transactions-{range.From:yyyyMMdd}-{range.To:yyyyMMdd}.csv");
    }

    [HttpGet("notes.json")]
    public async Task<IActionResult> Notes(CancellationToken cancellationToken)
    {
        var content = await exports.NotesJsonAsync(cancellationToken);
        return File(content, "application/json", "notes.json");
    }
}
