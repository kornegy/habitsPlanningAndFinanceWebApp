using Microsoft.AspNetCore.Mvc;
using PersonalPlatform.Application.Organization;
using PersonalPlatform.Contracts.Organization;

namespace PersonalPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]
public sealed class CategoriesController(OrganizationService organization) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CategoryDto>> List([FromQuery] CategoryScope? scope, CancellationToken cancellationToken) =>
        organization.ListCategoriesAsync(scope, cancellationToken);

    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await organization.CreateCategoryAsync(request, cancellationToken);
        return Created($"/api/v1/categories/{category.Id}", category);
    }

    [HttpPut("{id:guid}")]
    public Task<CategoryDto> Update(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken) =>
        organization.UpdateCategoryAsync(id, request, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await organization.DeleteCategoryAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/tags")]
public sealed class TagsController(OrganizationService organization) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TagDto>> List(CancellationToken cancellationToken) => organization.ListTagsAsync(cancellationToken);

    /// <summary>Creates a tag, or returns the existing tag with the same (case-insensitive) name.</summary>
    [HttpPost]
    public Task<TagDto> Create(CreateTagRequest request, CancellationToken cancellationToken) =>
        organization.CreateTagAsync(request, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await organization.DeleteTagAsync(id, cancellationToken);
        return NoContent();
    }
}
