using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Contracts.Tasks;
using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Finance;

namespace PersonalPlatform.Application.Finance;

/// <summary>
/// Recurring income/expense/transfer templates and the idempotent "post due occurrences" operation.
/// Posting runs per user on demand (the client calls it when finance views open), never as a background job
/// acting for all users at once.
/// </summary>
public sealed partial class RecurringTransactionService(
    IAppDbContext db,
    ICurrentUser currentUser,
    UserClock clock,
    TransactionService transactions,
    ILogger<RecurringTransactionService> logger)
{
    public const int MaxTemplatesPerUser = 100;
    private const string LockScope = "recurring-transactions";

    private Guid Owner => currentUser.UserId;

    public async Task<IReadOnlyList<RecurringTransactionDto>> ListAsync(CancellationToken cancellationToken)
    {
        var owner = Owner;
        var accounts = db.Accounts;
        var rows = await db.RecurringTransactions.AsNoTracking()
            .Where(r => r.OwnerId == owner)
            .OrderBy(r => r.NextOccurrence == null).ThenBy(r => r.NextOccurrence)
            .Select(r => new
            {
                Template = r,
                AccountName = accounts.Where(a => a.Id == r.AccountId && a.OwnerId == owner).Select(a => a.Name).First(),
                Currency = accounts.Where(a => a.Id == r.AccountId && a.OwnerId == owner).Select(a => a.CurrencyCode).First(),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => ToDto(r.Template, r.AccountName, r.Currency)).ToList();
    }

    public async Task<RecurringTransactionDto> CreateAsync(SaveRecurringTransactionRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        if (await db.RecurringTransactions.CountAsync(r => r.OwnerId == owner, cancellationToken) >= MaxTemplatesPerUser)
        {
            throw new ConflictException("limit_reached", $"At most {MaxTemplatesPerUser} recurring templates are allowed.");
        }

        var sample = await ResolveSampleAsync(request, cancellationToken);
        var template = new RecurringTransaction(owner, sample, request.Schedule.ToDomain(), clock.UtcNow);
        db.RecurringTransactions.Add(template);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(template.Id, cancellationToken);
    }

    /// <summary>Changes the template; already generated transactions are not modified.</summary>
    public async Task<RecurringTransactionDto> UpdateAsync(Guid id, SaveRecurringTransactionRequest request, CancellationToken cancellationToken)
    {
        var template = await LoadAsync(id, cancellationToken);
        db.SetExpectedVersion(template, request.Version ?? 0);
        var sample = await ResolveSampleAsync(request, cancellationToken);
        var today = (await clock.GetAsync(cancellationToken)).Today;
        template.Reconfigure(sample, request.Schedule.ToDomain(), today, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<RecurringTransactionDto> SetPausedAsync(Guid id, bool paused, CancellationToken cancellationToken)
    {
        var template = await LoadAsync(id, cancellationToken);
        if (paused)
        {
            template.Pause(clock.UtcNow);
        }
        else
        {
            template.Resume((await clock.GetAsync(cancellationToken)).Today, clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes the template; transactions it generated remain as ordinary transactions.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        if (!await db.RecurringTransactions.AnyAsync(r => r.Id == id && r.OwnerId == owner, cancellationToken))
        {
            throw new NotFoundException();
        }

        await db.Transactions.Where(t => t.OwnerId == owner && t.RecurringTransactionId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RecurringTransactionId, (Guid?)null), cancellationToken);
        await db.RecurringTransactions.Where(r => r.Id == id && r.OwnerId == owner).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Posts every occurrence due up to the owner's today. Safe to call repeatedly and concurrently: calls of
    /// one user are serialised by an advisory lock, and each occurrence is unique per (template, date).
    /// A template that can no longer produce a valid transaction (e.g. its account was archived) is paused.
    /// </summary>
    public async Task<PostDueResult> PostDueAsync(CancellationToken cancellationToken)
    {
        var owner = Owner;
        var today = (await clock.GetAsync(cancellationToken)).Today;
        var now = clock.UtcNow;
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockUserScopeAsync(owner, LockScope, cancellationToken);

        var templates = await db.RecurringTransactions
            .Where(r => r.OwnerId == owner && !r.IsPaused && r.NextOccurrence != null && r.NextOccurrence <= today)
            .ToListAsync(cancellationToken);
        var posted = 0;
        foreach (var template in templates)
        {
            var dates = template.DueDates(today);
            if (dates.Count == 0)
            {
                continue;
            }

            var alreadyPosted = await db.Transactions
                .Where(t => t.OwnerId == owner && t.RecurringTransactionId == template.Id && t.RecurrenceDate != null && dates.Contains(t.RecurrenceDate.Value))
                .Select(t => t.RecurrenceDate!.Value)
                .ToListAsync(cancellationToken);
            try
            {
                // Build every occurrence first so a failing template contributes nothing.
                var generated = new List<FinancialTransaction>();
                foreach (var date in dates.Except(alreadyPosted))
                {
                    var details = await transactions.ResolveAsync(template.Kind, date, template.Description, template.AccountId, template.Amount, template.CategoryId, template.DestinationAccountId, template.DestinationAmount, cancellationToken);
                    var occurrence = FinancialTransaction.Create(owner, details, now);
                    occurrence.AttachRecurrence(template.Id, date);
                    generated.Add(occurrence);
                }

                db.Transactions.AddRange(generated);
                posted += generated.Count;
                template.MarkPostedThrough(dates[^1], now);
            }
            catch (Exception ex) when (ex is DomainRuleException or NotFoundException)
            {
                template.Pause(now);
                LogTemplatePaused(logger, template.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PostDueResult(posted);
    }

    private async Task<RecurringTransactionDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).FirstOrDefault(r => r.Id == id) ?? throw new NotFoundException();

    private async Task<RecurringTransaction> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        return await db.RecurringTransactions.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
    }

    private Task<TransactionDetails> ResolveSampleAsync(SaveRecurringTransactionRequest r, CancellationToken cancellationToken) =>
        transactions.ResolveAsync(r.Kind.ToDomain(), r.Schedule.StartDate, r.Description, r.AccountId, r.Amount, r.CategoryId, r.DestinationAccountId, r.DestinationAmount, cancellationToken);

    private static RecurringTransactionDto ToDto(RecurringTransaction r, string accountName, string currency) => new(
        r.Id, r.Kind.ToContract(), r.AccountId, accountName, r.DestinationAccountId, r.DestinationAmount, r.CategoryId, r.Amount, currency,
        r.Description, r.Pattern.ToContract(), r.NextOccurrence, r.IsPaused, r.Version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recurring template {TemplateId} paused: it no longer produces a valid transaction")]
    private static partial void LogTemplatePaused(ILogger logger, Guid templateId);
}

public sealed class SaveRecurringTransactionRequestValidator : AbstractValidator<SaveRecurringTransactionRequest>
{
    public SaveRecurringTransactionRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Amount).ValidAmount();
        RuleFor(x => x.DestinationAmount!.Value).ValidAmount().When(x => x.DestinationAmount is not null);
        RuleFor(x => x.Description).MaximumLength(FinancialTransaction.DescriptionMaxLength);
        RuleFor(x => x.Schedule).NotNull().SetValidator(new RecurrenceDtoValidator());
        RuleFor(x => x.Schedule.Frequency).NotEqual(RecurrenceFrequency.Daily)
            .When(x => x.Schedule is not null).WithMessage("Daily recurring transactions are not supported; use weekly or longer.");
    }
}
