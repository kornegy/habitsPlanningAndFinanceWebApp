using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Domain.Finance;
using DomainAccount = PersonalPlatform.Domain.Finance.Account;

namespace PersonalPlatform.Application.Finance;

/// <summary>
/// Money accounts. Balances are computed on every read as opening balance + SUM(ledger entries); no stored
/// balance exists that could drift or be set by a client.
/// </summary>
public sealed class AccountService(IAppDbContext db, ICurrentUser currentUser, UserClock clock)
{
    public const int MaxAccountsPerUser = 100;

    private Guid Owner => currentUser.UserId;

    public async Task<AccountsOverviewDto> ListAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var accounts = await Projection(Owner, db.Accounts.Where(a => includeArchived || a.ArchivedAt == null))
            .ToListAsync(cancellationToken);
        var totals = accounts
            .Where(a => !a.IsArchived)
            .GroupBy(a => a.CurrencyCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CurrencyAmount(g.Key, g.Sum(a => a.Balance)))
            .ToList();
        return new AccountsOverviewDto(accounts, totals);
    }

    public async Task<AccountDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await Projection(Owner, db.Accounts.Where(a => a.Id == id)).FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException();

    public async Task<AccountDto> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        if (await db.Accounts.CountAsync(a => a.OwnerId == owner, cancellationToken) >= MaxAccountsPerUser)
        {
            throw new ConflictException("limit_reached", $"At most {MaxAccountsPerUser} accounts are allowed.");
        }

        var today = (await clock.GetAsync(cancellationToken)).Today;
        var account = new DomainAccount(owner, request.Name, request.Type.ToDomain(), request.CurrencyCode, request.OpeningBalance, request.OpeningDate ?? today, clock.UtcNow);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(account.Id, cancellationToken);
    }

    public async Task<AccountDto> UpdateAsync(Guid id, UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await LoadAsync(id, cancellationToken);
        db.SetExpectedVersion(account, request.Version);
        account.Update(request.Name, request.Type.ToDomain(), request.OpeningBalance, request.OpeningDate, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<AccountDto> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken)
    {
        var account = await LoadAsync(id, cancellationToken);
        if (archived)
        {
            account.Archive(clock.UtcNow);
        }
        else
        {
            account.Restore(clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Deletes an account only if nothing references it. Accounts with history must be archived instead, so
    /// that deleting one can never silently change other accounts' balances (transfers) or reports.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var account = await LoadAsync(id, cancellationToken);
        var used = await db.LedgerEntries.AnyAsync(e => e.OwnerId == owner && e.AccountId == id, cancellationToken)
            || await db.RecurringTransactions.AnyAsync(r => r.OwnerId == owner && (r.AccountId == id || r.DestinationAccountId == id), cancellationToken);
        if (used)
        {
            throw new ConflictException("account_in_use", "The account has transactions or recurring templates. Archive it instead.");
        }

        db.Accounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<DomainAccount> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        return await db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
    }

    private IQueryable<AccountDto> Projection(Guid owner, IQueryable<DomainAccount> accounts)
    {
        var entries = db.LedgerEntries;
        return accounts.AsNoTracking()
            .Where(a => a.OwnerId == owner)
            .OrderBy(a => a.ArchivedAt != null).ThenBy(a => a.Name).ThenBy(a => a.Id)
            .Select(a => new AccountDto(
                a.Id,
                a.Name,
                (Contracts.Finance.AccountType)a.Type,
                a.CurrencyCode,
                a.OpeningBalance,
                a.OpeningDate,
                a.OpeningBalance + (entries.Where(e => e.AccountId == a.Id && e.OwnerId == owner).Sum(e => (decimal?)e.Amount) ?? 0m),
                a.ArchivedAt != null,
                a.Version));
    }
}

internal static class MoneyRules
{
    public static IRuleBuilderOptions<T, decimal> ValidAmount<T>(this IRuleBuilder<T, decimal> rule, bool allowNegative = false) =>
        rule.Must(a => (allowNegative || a > 0) && Currencies.IsWithinRange(a))
            .WithMessage(allowNegative ? "Amount is out of range." : "Amount must be greater than zero and within range.")
            .Must(a => decimal.Round(a, 4) == a).WithMessage("Amount has too many decimal places.");

    public static IRuleBuilderOptions<T, string> ValidCurrency<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(Currencies.IsSupported).WithMessage("Unsupported currency.");
}

public sealed class CreateAccountRequestValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainAccount.NameMaxLength);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.CurrencyCode).ValidCurrency();
        RuleFor(x => x.OpeningBalance).ValidAmount(allowNegative: true);
        RuleFor(x => x.OpeningDate).ReasonableDate();
    }
}

public sealed class UpdateAccountRequestValidator : AbstractValidator<UpdateAccountRequest>
{
    public UpdateAccountRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainAccount.NameMaxLength);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.OpeningBalance).ValidAmount(allowNegative: true);
        RuleFor(x => x.OpeningDate).ReasonableDate();
    }
}
