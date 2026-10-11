using Microsoft.AspNetCore.Mvc;
using PersonalPlatform.Application.Notes;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Notes;

namespace PersonalPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/notes")]
public sealed class NotesController(NoteService notes) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<NoteSummaryDto>> List([FromQuery] NoteQuery query, CancellationToken cancellationToken) =>
        notes.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<NoteDto> Get(Guid id, CancellationToken cancellationToken) => notes.GetAsync(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType<NoteDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateNoteRequest request, CancellationToken cancellationToken)
    {
        var note = await notes.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/notes/{note.Id}", note);
    }

    [HttpPut("{id:guid}")]
    public Task<NoteDto> Update(Guid id, UpdateNoteRequest request, CancellationToken cancellationToken) =>
        notes.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/pin")]
    public Task<NoteDto> SetPinned(Guid id, SetPinnedRequest request, CancellationToken cancellationToken) =>
        notes.SetPinnedAsync(id, request.IsPinned, cancellationToken);

    [HttpPost("{id:guid}/archive")]
    public Task<NoteDto> SetArchived(Guid id, SetArchivedRequest request, CancellationToken cancellationToken) =>
        notes.SetArchivedAsync(id, request.IsArchived, cancellationToken);

    [HttpGet("{id:guid}/revisions")]
    public Task<IReadOnlyList<NoteRevisionDto>> Revisions(Guid id, CancellationToken cancellationToken) =>
        notes.RevisionsAsync(id, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await notes.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/journal")]
public sealed class JournalController(JournalService journal) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<JournalEntryDto>> List([FromQuery] JournalQuery query, CancellationToken cancellationToken) =>
        journal.ListAsync(query, cancellationToken);

    [HttpGet("{date}")]
    public Task<JournalEntryDto> Get(DateOnly date, CancellationToken cancellationToken) => journal.GetAsync(date, cancellationToken);

    [HttpPut("{date}")]
    public Task<JournalEntryDto> Save(DateOnly date, SaveJournalEntryRequest request, CancellationToken cancellationToken) =>
        journal.SaveAsync(date, request, cancellationToken);

    [HttpDelete("{date}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(DateOnly date, CancellationToken cancellationToken)
    {
        await journal.DeleteAsync(date, cancellationToken);
        return NoContent();
    }
}
