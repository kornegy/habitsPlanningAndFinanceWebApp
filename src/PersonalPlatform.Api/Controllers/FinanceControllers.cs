using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalPlatform.Api.Infrastructure;
using PersonalPlatform.Application.Finance;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Contracts.Notes;

namespace PersonalPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/finance/accounts")]
public sealed class AccountsController(AccountService accounts) : ControllerBase
{
    [HttpGet]
    public Task<AccountsOverviewDto> List([FromQuery] bool includeArchived, CancellationToken cancellationToken) =>
        accounts.ListAsync(includeArchived, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<AccountDto> Get(Guid id, CancellationToken cancellationToken) => accounts.GetAsync(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<AccountDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await accounts.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/finance/accounts/{account.Id}", account);
    }

    [HttpPut("{id:guid}")]
    public Task<AccountDto> Update(Guid id, UpdateAccountRequest request, CancellationToken cancellationToken) =>
        accounts.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/archive")]
    public Task<AccountDto> SetArchived(Guid id, SetArchivedRequest request, CancellationToken cancellationToken) =>
        accounts.SetArchivedAsync(id, request.IsArchived, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await accounts.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/finance/categories")]
public sealed class FinanceCategoriesController(FinanceCategoryService categories) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<FinanceCategoryDto>> List(CancellationToken cancellationToken) => categories.ListAsync(cancellationToken);

    [HttpPost]
    [ProducesResponseType<FinanceCategoryDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateFinanceCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await categories.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/finance/categories/{category.Id}", category);
    }

    [HttpPut("{id:guid}")]
    public Task<FinanceCategoryDto> Update(Guid id, UpdateFinanceCategoryRequest request, CancellationToken cancellationToken) =>
        categories.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await categories.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/finance/transactions")]
public sealed class TransactionsController(TransactionService transactions) : ControllerBase
{
    public const string ReplayedHeader = "Idempotency-Replayed";

    [HttpGet]
    public Task<PagedResult<TransactionDto>> List([FromQuery] TransactionQuery query, CancellationToken cancellationToken) =>
        transactions.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<TransactionDto> Get(Guid id, CancellationToken cancellationToken) => transactions.GetAsync(id, cancellationToken);

    /// <summary>
    /// Creates an operation. With an <c>Idempotency-Key</c> header a retry returns the original operation (200 and
    /// <c>Idempotency-Replayed: true</c>) instead of creating a duplicate.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(
        CreateTransactionRequest request,
        [FromHeader(Name = ApiHeaders.IdempotencyKey)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is not null && !IdempotencyKeys.IsValid(idempotencyKey))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid Idempotency-Key (8-64 characters: letters, digits, '-', '_').");
        }

        var result = await transactions.CreateAsync(request, idempotencyKey, cancellationToken);
        if (result.Replayed)
        {
            Response.Headers[ReplayedHeader] = "true";
            return Ok(result.Transaction);
        }

        return Created($"/api/v1/finance/transactions/{result.Transaction.Id}", result.Transaction);
    }

    [HttpPut("{id:guid}")]
    public Task<TransactionDto> Update(Guid id, UpdateTransactionRequest request, CancellationToken cancellationToken) =>
        transactions.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await transactions.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/finance/budgets")]
public sealed class BudgetsController(BudgetService budgets) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<BudgetDto>> List(CancellationToken cancellationToken) => budgets.ListAsync(cancellationToken);

    [HttpPost]
    [ProducesResponseType<BudgetDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateBudgetRequest request, CancellationToken cancellationToken)
    {
        var budget = await budgets.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/finance/budgets/{budget.Id}", budget);
    }

    [HttpPut("{id:guid}")]
    public Task<BudgetDto> Update(Guid id, UpdateBudgetRequest request, CancellationToken cancellationToken) =>
        budgets.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await budgets.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("report")]
    [EnableRateLimiting(RateLimiting.Reports)]
    public Task<BudgetReportDto> Report([FromQuery] MonthQuery month, CancellationToken cancellationToken) =>
        budgets.ReportAsync(month.Year, month.Month, cancellationToken);
}

[ApiController]
[Route("api/v1/finance/recurring")]
public sealed class RecurringTransactionsController(RecurringTransactionService recurring) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RecurringTransactionDto>> List(CancellationToken cancellationToken) => recurring.ListAsync(cancellationToken);

    [HttpPost]
    [ProducesResponseType<RecurringTransactionDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveRecurringTransactionRequest request, CancellationToken cancellationToken)
    {
        var template = await recurring.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/finance/recurring/{template.Id}", template);
    }

    [HttpPut("{id:guid}")]
    public Task<RecurringTransactionDto> Update(Guid id, SaveRecurringTransactionRequest request, CancellationToken cancellationToken) =>
        recurring.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/pause")]
    public Task<RecurringTransactionDto> SetPaused(Guid id, SetPausedRequest request, CancellationToken cancellationToken) =>
        recurring.SetPausedAsync(id, request.IsPaused, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await recurring.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Posts all occurrences due up to today. Idempotent and safe to call repeatedly.</summary>
    [HttpPost("post-due")]
    [EnableRateLimiting(RateLimiting.Reports)]
    public Task<PostDueResult> PostDue(CancellationToken cancellationToken) => recurring.PostDueAsync(cancellationToken);
}

[ApiController]
[Route("api/v1/finance/reports")]
[EnableRateLimiting(RateLimiting.Reports)]
public sealed class FinanceReportsController(FinanceReportService reports) : ControllerBase
{
    [HttpGet("year")]
    public Task<YearSummaryDto> Year([FromQuery] YearQuery query, CancellationToken cancellationToken) =>
        reports.YearSummaryAsync(query.Year, cancellationToken);

    [HttpGet("period")]
    public Task<PeriodSummaryDto> Period([FromQuery] DateRangeQuery range, CancellationToken cancellationToken) =>
        reports.PeriodSummaryAsync(range.From, range.To, cancellationToken);

    [HttpGet("categories")]
    public Task<CategoryBreakdownDto> Categories([FromQuery] CategoryBreakdownQuery query, CancellationToken cancellationToken) =>
        reports.CategoryBreakdownAsync(query, cancellationToken);
}
