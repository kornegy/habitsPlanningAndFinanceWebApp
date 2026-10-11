namespace PersonalPlatform.Contracts.Organization;

public enum CategoryScope
{
    Task = 0,
    Habit = 1,
}

public sealed record CategoryDto(Guid Id, CategoryScope Scope, string Name, string Color, uint Version);

public sealed record CreateCategoryRequest(CategoryScope Scope, string Name, string? Color);

public sealed record UpdateCategoryRequest(string Name, string? Color, uint Version);

public sealed record TagDto(Guid Id, string Name);

public sealed record CreateTagRequest(string Name);
