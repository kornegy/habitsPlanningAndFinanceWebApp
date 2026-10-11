using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Domain.Finance;

namespace PersonalPlatform.Application.Finance;

/// <summary>Income/expense categories with at most one level of subcategories.</summary>
public sealed class FinanceCategoryService(IAppDbContext db, ICurrentUser currentUser, UserClock clock)
{
    public const int MaxCategoriesPerUser = 300;

    private Guid Owner => currentUser.UserId;

    public async Task<IReadOnlyList<FinanceCategoryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var owner = Owner;
        return await db.FinanceCategories.AsNoTracking()
            .Where(c => c.OwnerId == owner)
            .OrderBy(c => c.Kind).ThenBy(c => c.Name)
            .Select(c => new FinanceCategoryDto(c.Id, c.Name, (Contracts.Finance.CategoryKind)c.Kind, c.ParentId, c.ArchivedAt != null, c.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<FinanceCategoryDto> CreateAsync(CreateFinanceCategoryRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        if (await db.FinanceCategories.CountAsync(c => c.OwnerId == owner, cancellationToken) >= MaxCategoriesPerUser)
        {
            throw new ConflictException("limit_reached", $"At most {MaxCategoriesPerUser} categories are allowed.");
        }

        var parent = request.ParentId is { } parentId ? await LoadAsync(parentId, cancellationToken) : null;
        var category = new FinanceCategory(owner, request.Name, request.Kind.ToDomain(), parent, clock.UtcNow);
        db.FinanceCategories.Add(category);
        await SaveAsync(cancellationToken);
        return ToDto(category);
    }

    public async Task<FinanceCategoryDto> UpdateAsync(Guid id, UpdateFinanceCategoryRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var category = await LoadAsync(id, cancellationToken);
        db.SetExpectedVersion(category, request.Version);
        var now = clock.UtcNow;
        category.Rename(request.Name, now);

        if (request.ParentId != category.ParentId)
        {
            if (request.ParentId is not null && await db.FinanceCategories.AnyAsync(c => c.OwnerId == owner && c.ParentId == id, cancellationToken))
            {
                throw new ConflictException("category_has_children", "A category with subcategories cannot become a subcategory.");
            }

            category.SetParent(request.ParentId is { } parentId ? await LoadAsync(parentId, cancellationToken) : null);
        }

        if (request.IsArchived)
        {
            category.Archive(now);
        }
        else
        {
            category.Restore(now);
        }

        await SaveAsync(cancellationToken);
        return ToDto(category);
    }

    /// <summary>
    /// Deletes a category. Its transactions and templates become uncategorised; its budgets are removed.
    /// A category that still has subcategories cannot be deleted.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var category = await LoadAsync(id, cancellationToken);
        if (await db.FinanceCategories.AnyAsync(c => c.OwnerId == owner && c.ParentId == id, cancellationToken))
        {
            throw new ConflictException("category_has_children", "Delete or move the subcategories first.");
        }

        await db.Transactions.Where(t => t.OwnerId == owner && t.CategoryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, (Guid?)null), cancellationToken);
        await db.RecurringTransactions.Where(r => r.OwnerId == owner && r.CategoryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CategoryId, (Guid?)null), cancellationToken);
        db.FinanceCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal async Task<FinanceCategory> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        return await db.FinanceCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException("Category not found.");
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException ex)
        {
            throw new ConflictException("duplicate", "A category with this name already exists here.", ex);
        }
    }

    private static FinanceCategoryDto ToDto(FinanceCategory c) =>
        new(c.Id, c.Name, c.Kind.ToContract(), c.ParentId, c.ArchivedAt is not null, c.Version);
}

public sealed class CreateFinanceCategoryRequestValidator : AbstractValidator<CreateFinanceCategoryRequest>
{
    public CreateFinanceCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(FinanceCategory.NameMaxLength);
        RuleFor(x => x.Kind).IsInEnum();
    }
}

public sealed class UpdateFinanceCategoryRequestValidator : AbstractValidator<UpdateFinanceCategoryRequest>
{
    public UpdateFinanceCategoryRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(FinanceCategory.NameMaxLength);
}
