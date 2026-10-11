using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Contracts.Organization;
using PersonalPlatform.Domain.Organization;
using DomainCategory = PersonalPlatform.Domain.Organization.Category;
using DomainScope = PersonalPlatform.Domain.Organization.CategoryScope;

namespace PersonalPlatform.Application.Organization;

/// <summary>Task/habit categories and tags. Every query is restricted to the current owner.</summary>
public sealed class OrganizationService(IAppDbContext db, ICurrentUser currentUser, UserClock clock)
{
    public const int MaxCategoriesPerUser = 200;
    public const int MaxTagsPerUser = 500;

    private Guid Owner => currentUser.UserId;

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(Contracts.Organization.CategoryScope? scope, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var query = db.Categories.AsNoTracking().Where(c => c.OwnerId == owner);
        if (scope is { } s)
        {
            var domainScope = s.ToDomain();
            query = query.Where(c => c.Scope == domainScope);
        }

        return await query.OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, (Contracts.Organization.CategoryScope)c.Scope, c.Name, c.Color, c.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        if (await db.Categories.CountAsync(c => c.OwnerId == owner, cancellationToken) >= MaxCategoriesPerUser)
        {
            throw new ConflictException("limit_reached", $"At most {MaxCategoriesPerUser} categories are allowed.");
        }

        var category = new DomainCategory(owner, request.Scope.ToDomain(), request.Name, request.Color, clock.UtcNow);
        db.Categories.Add(category);
        await SaveUniqueAsync("A category with this name already exists.", cancellationToken);
        return ToDto(category);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();
        db.SetExpectedVersion(category, request.Version);
        category.Update(request.Name, request.Color);
        await SaveUniqueAsync("A category with this name already exists.", cancellationToken);
        return ToDto(category);
    }

    /// <summary>Deleting a category keeps its tasks, series and habits; they simply become uncategorised.</summary>
    public async Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == owner, cancellationToken)
            ?? throw new NotFoundException();

        await db.Tasks.Where(t => t.OwnerId == owner && t.CategoryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, (Guid?)null), cancellationToken);
        await db.TaskSeries.Where(t => t.OwnerId == owner && t.CategoryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, (Guid?)null), cancellationToken);
        await db.Habits.Where(h => h.OwnerId == owner && h.CategoryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.CategoryId, (Guid?)null), cancellationToken);
        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TagDto>> ListTagsAsync(CancellationToken cancellationToken)
    {
        var owner = Owner;
        return await db.Tags.AsNoTracking()
            .Where(t => t.OwnerId == owner)
            .OrderBy(t => t.NormalizedName)
            .Select(t => new TagDto(t.Id, t.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<TagDto> CreateTagAsync(CreateTagRequest request, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var normalized = Tag.Normalize(request.Name);
        var existing = await db.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.OwnerId == owner && t.NormalizedName == normalized, cancellationToken);
        if (existing is not null)
        {
            return new TagDto(existing.Id, existing.Name); // idempotent: the same name yields the same tag
        }

        if (await db.Tags.CountAsync(t => t.OwnerId == owner, cancellationToken) >= MaxTagsPerUser)
        {
            throw new ConflictException("limit_reached", $"At most {MaxTagsPerUser} tags are allowed.");
        }

        var tag = new Tag(owner, request.Name, clock.UtcNow);
        db.Tags.Add(tag);
        await SaveUniqueAsync("A tag with this name already exists.", cancellationToken);
        return new TagDto(tag.Id, tag.Name);
    }

    /// <summary>Deleting a tag removes it from all tasks and notes (join rows cascade).</summary>
    public async Task DeleteTagAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = Owner;
        var deleted = await db.Tags.Where(t => t.Id == id && t.OwnerId == owner).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw new NotFoundException();
        }
    }

    /// <summary>
    /// Loads the given tags, failing with 404 if any id is unknown or belongs to someone else (indistinguishable).
    /// </summary>
    public static async Task<IReadOnlyList<Tag>> LoadOwnedTagsAsync(IAppDbContext db, Guid owner, IReadOnlyList<Guid>? ids, CancellationToken cancellationToken)
    {
        if (ids is null || ids.Count == 0)
        {
            return [];
        }

        var tags = await db.Tags.Where(t => t.OwnerId == owner && ids.Contains(t.Id)).ToListAsync(cancellationToken);
        return tags.Count == ids.Distinct().Count() ? tags : throw new NotFoundException("Tag not found.");
    }

    /// <summary>Ensures an optional category exists, belongs to the owner and has the expected scope.</summary>
    public static async Task EnsureCategoryAsync(IAppDbContext db, Guid owner, Guid? categoryId, DomainScope scope, CancellationToken cancellationToken)
    {
        if (categoryId is null)
        {
            return;
        }

        var exists = await db.Categories.AnyAsync(c => c.Id == categoryId && c.OwnerId == owner && c.Scope == scope, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Category not found.");
        }
    }

    private async Task SaveUniqueAsync(string duplicateMessage, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException ex)
        {
            throw new ConflictException("duplicate", duplicateMessage, ex);
        }
    }

    private static CategoryDto ToDto(DomainCategory c) => new(c.Id, c.Scope.ToContract(), c.Name, c.Color, c.Version);
}

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Scope).IsInEnum();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainCategory.NameMaxLength);
        RuleFor(x => x.Color).Must(c => c is null || Domain.Common.ColorHex.IsValid(c)).WithMessage("Color must be in #RRGGBB format.");
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainCategory.NameMaxLength);
        RuleFor(x => x.Color).Must(c => c is null || Domain.Common.ColorHex.IsValid(c)).WithMessage("Color must be in #RRGGBB format.");
    }
}

public sealed class CreateTagRequestValidator : AbstractValidator<CreateTagRequest>
{
    public CreateTagRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(Tag.NameMaxLength);
}
