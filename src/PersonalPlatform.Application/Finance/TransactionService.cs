using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Domain.Finance;
using DomainKind = PersonalPlatform.Domain.Finance.TransactionKind;

namespace PersonalPlatform.Application.Finance;

/// <summary>Result of a create call; <see cref="Replayed"/> is true when an idempotent retry returned the original.</summary>
public sealed record CreateTransactionResult(TransactionDto Transaction, bool Replayed);

/// <summary>
/// Income, expense and transfer operations.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Each operation and its ledger entries are written in one SaveChanges (one database transaction),
/// so a transfer's debit and credit are always both present or both absent.</item>
/// <item>Creating with an <c>Idempotency-Key</c> is safe to retry: the same key with the same body returns the
/// original operation; the same key with a different body is rejected (409). A unique index makes this hold
/// under concurrency.</item>
/// <item>Updates require the version the client read (optimistic concurrency); stale versions yield 409.</item>
/// <item>Accounts and categories are resolved with an owner filter, so foreign ids behave like missing ones,
/// and the database's composite foreign keys reject cross-user references as a second line of defence.</item>
/// </list>
/// </remarks>
public sealed class TransactionService(IAppDbContext db, ICurrentUser currentUser, UserClock clock)
{
    private Guid Owner => currentUser.UserId;

    public async Task<PagedResult<TransactionDto>> ListAsync(TransactionQuery query, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var transactions = db.Transactions.AsNoTracking().Where(t => t.OwnerId == owner);
        if (query.From is { } from)
        {
            transactions = transactions.Where(t => t.OccurredOn >= from);
        }

        if (query.To is { } to)
        {
            transactions = transactions.Where(t => t.OccurredOn <= to);
        }

        if (query.AccountId is { } accountId)
        {
            transactions = transactions.Where(t => t.AccountId == accountId || t.DestinationAccountId == accountId);
        }

        if (query.CategoryId is { } categoryId)
        {
            // A parent category also matches its subcategories.
            var categoryIds = db.FinanceCategories.Where(c => c.OwnerId == owner && (c.Id == categoryId || c.ParentId == categoryId)).Select(c => c.Id);
            transactions = transactions.Where(t => t.CategoryId != null && categoryIds.Contains(t.CategoryId.Value));
        }

        if (query.Kind is { } kind)
        {
            var domainKind = kind.ToDomain();
            transactions = transactions.Where(t => t.Kind == domainKind);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            transactions = transactions.Where(t => t.Description != null && t.Description.ToLower().Contains(term));
        }

        IOrderedQueryable<FinancialTransaction> ordered = query.Sort switch
        {
            TransactionSort.DateAsc => transactions.OrderBy(t => t.OccurredOn).ThenBy(t => t.CreatedAt),
            TransactionSort.AmountDesc => transactions.OrderByDescending(t => t.Amount).ThenByDescending(t => t.OccurredOn),
            TransactionSort.AmountAsc => transactions.OrderBy(t => t.Amount).ThenByDescending(t => t.OccurredOn),
            _ => transactions.OrderByDescending(t => t.OccurredOn).ThenByDescending(t => t.CreatedAt),
        };

        return await ordered.ThenBy(t => t.Id).Select(Projection(owner)).ToPagedResultAsync(query.Page, query.PageSize, cancellationToken);
    }

    public async Task<TransactionDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        return await db.Transactions.AsNoTracking()
            .Where(t => t.Id == id && t.OwnerId == owner)
            .Select(Projection(owner))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException();
    }

    public async Task<CreateTransactionResult> CreateAsync(CreateTransactionRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var fingerprint = Fingerprint(request);
        if (idempotencyKey is not null && await FindByIdempotencyKeyAsync(idempotencyKey, fingerprint, cancellationToken) is { } previous)
        {
            return new CreateTransactionResult(previous, Replayed: true);
        }

        var details = await ResolveAsync(request.Kind.ToDomain(), request.OccurredOn, request.Description, request.AccountId, request.Amount, request.CategoryId, request.DestinationAccountId, request.DestinationAmount, cancellationToken);
        var transaction = FinancialTransaction.Create(owner, details, clock.UtcNow);
        if (idempotencyKey is not null)
        {
            transaction.AttachIdempotency(idempotencyKey, fingerprint);
        }

        db.Transactions.Add(transaction);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException) when (idempotencyKey is not null)
        {
            // A concurrent request with the same key won the race; return its result.
            db.ResetTracking();
            var winner = await FindByIdempotencyKeyAsync(idempotencyKey, fingerprint, cancellationToken)
                ?? throw new ConflictException("idempotency_conflict", "The request could not be completed; retry.");
            return new CreateTransactionResult(winner, Replayed: true);
        }

        return new CreateTransactionResult(await GetAsync(transaction.Id, cancellationToken), Replayed: false);
    }

    public async Task<TransactionDto> UpdateAsync(Guid id, UpdateTransactionRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var transaction = await db.Transactions.Include(t => t.Entries)
            .FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
        db.SetExpectedVersion(transaction, request.Version);
        var details = await ResolveAsync(transaction.Kind, request.OccurredOn, request.Description, request.AccountId, request.Amount, request.CategoryId, request.DestinationAccountId, request.DestinationAmount, cancellationToken);

        // Old entries are deleted and new ones inserted in the same database transaction as the header update.
        transaction.Update(details, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes the operation and its ledger entries atomically (cascade).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var deleted = await db.Transactions.Where(t => t.Id == id && t.OwnerId == owner).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw new NotFoundException();
        }
    }

    /// <summary>Loads and validates every referenced object with an owner filter.</summary>
    internal async Task<TransactionDetails> ResolveAsync(
        DomainKind kind,
        DateOnly occurredOn,
        string? description,
        Guid accountId,
        decimal amount,
        Guid? categoryId,
        Guid? destinationAccountId,
        decimal? destinationAmount,
        CancellationToken cancellationToken)
    {
        var owner = Owner;
        var accountIds = new[] { accountId, destinationAccountId ?? accountId };
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.OwnerId == owner && accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var source = accounts.GetValueOrDefault(accountId) ?? throw new NotFoundException("Account not found.");
        Account? destination = null;
        if (destinationAccountId is { } destinationId)
        {
            destination = accounts.GetValueOrDefault(destinationId) ?? throw new NotFoundException("Account not found.");
        }

        FinanceCategory? category = null;
        if (categoryId is { } cid)
        {
            category = await db.FinanceCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid && c.OwnerId == owner, cancellationToken)
                ?? throw new NotFoundException("Category not found.");
        }

        return new TransactionDetails(kind, occurredOn, description, source, amount, category, destination, destinationAmount);
    }

    private async Task<TransactionDto?> FindByIdempotencyKeyAsync(string key, string fingerprint, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var existing = await db.Transactions.AsNoTracking()
            .Where(t => t.OwnerId == owner && t.IdempotencyKey == key)
            .Select(t => new { t.Id, t.RequestFingerprint })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (existing.RequestFingerprint != fingerprint)
        {
            throw new ConflictException("idempotency_key_reused", "This Idempotency-Key was already used for a different request.");
        }

        return await GetAsync(existing.Id, cancellationToken);
    }

    /// <summary>Stable hash of the request body; detects reuse of an idempotency key with different content.</summary>
    internal static string Fingerprint(CreateTransactionRequest r)
    {
        var canonical = string.Join(
            '|',
            r.Kind,
            r.OccurredOn.ToString("O", CultureInfo.InvariantCulture),
            r.Description ?? string.Empty,
            r.AccountId,
            r.Amount.ToString(CultureInfo.InvariantCulture),
            r.CategoryId?.ToString() ?? string.Empty,
            r.DestinationAccountId?.ToString() ?? string.Empty,
            r.DestinationAmount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private Expression<Func<FinancialTransaction, TransactionDto>> Projection(Guid owner)
    {
        var accounts = db.Accounts;
        var categories = db.FinanceCategories;
        return t => new TransactionDto(
            t.Id,
            (Contracts.Finance.TransactionKind)t.Kind,
            t.OccurredOn,
            t.Description,
            t.CategoryId,
            categories.Where(c => c.Id == t.CategoryId && c.OwnerId == owner).Select(c => c.Name).FirstOrDefault(),
            t.AccountId,
            accounts.Where(a => a.Id == t.AccountId && a.OwnerId == owner).Select(a => a.Name).First(),
            t.Amount,
            t.CurrencyCode,
            t.DestinationAccountId,
            accounts.Where(a => a.Id == t.DestinationAccountId && a.OwnerId == owner).Select(a => a.Name).FirstOrDefault(),
            t.DestinationAmount,
            t.DestinationCurrencyCode,
            t.RecurringTransactionId,
            t.CreatedAt,
            t.UpdatedAt,
            t.Version);
    }
}

public sealed class CreateTransactionRequestValidator : AbstractValidator<CreateTransactionRequest>
{
    public CreateTransactionRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.OccurredOn).ReasonableDate();
        RuleFor(x => x.Description).MaximumLength(FinancialTransaction.DescriptionMaxLength);
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Amount).ValidAmount();
        RuleFor(x => x.DestinationAmount!.Value).ValidAmount().When(x => x.DestinationAmount is not null);
        RuleFor(x => x.DestinationAccountId).NotEmpty().When(x => x.Kind == Contracts.Finance.TransactionKind.Transfer);
        RuleFor(x => x.DestinationAccountId).Null().When(x => x.Kind != Contracts.Finance.TransactionKind.Transfer);
        RuleFor(x => x.DestinationAmount).Null().When(x => x.Kind != Contracts.Finance.TransactionKind.Transfer);
        RuleFor(x => x.CategoryId).Null().When(x => x.Kind == Contracts.Finance.TransactionKind.Transfer);
    }
}

public sealed class UpdateTransactionRequestValidator : AbstractValidator<UpdateTransactionRequest>
{
    public UpdateTransactionRequestValidator()
    {
        RuleFor(x => x.OccurredOn).ReasonableDate();
        RuleFor(x => x.Description).MaximumLength(FinancialTransaction.DescriptionMaxLength);
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Amount).ValidAmount();
        RuleFor(x => x.DestinationAmount!.Value).ValidAmount().When(x => x.DestinationAmount is not null);
    }
}

public sealed class TransactionQueryValidator : AbstractValidator<TransactionQuery>
{
    public TransactionQueryValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Sort).IsInEnum();
        RuleFor(x => x.Search).ValidSearch();
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        this.ValidRange(x => x.From, x => x.To, 3660);
    }
}

public static class IdempotencyKeys
{
    /// <summary>Accepts 8–64 characters of [A-Za-z0-9_-] (e.g. a UUID chosen by the client per user intent).</summary>
    public static bool IsValid(string? key) =>
        key is { Length: >= 8 and <= FinancialTransaction.IdempotencyKeyMaxLength }
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
