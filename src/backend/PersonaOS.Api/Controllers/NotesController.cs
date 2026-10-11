using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Api.Infrastructure;
using PersonaOS.Application.Board;
using PersonaOS.Application.Notes;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// Notes: things jotted down to deal with later. List, add, edit, delete, and move one to the
/// board's backlog (which removes the note).
/// </summary>
[ApiController]
[Route("api/notes")]
[Authorize]
[RequireFeature(InstanceConfig.Modules.Notes)]
public class NotesController(INoteService notes) : ControllerBase
{
    public record SaveNoteRequest(string? Text);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NoteDto>>> List([FromQuery] string? search, CancellationToken ct) =>
        Ok(await notes.ListAsync(search, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<NoteDto>> Get(int id, CancellationToken ct) =>
        await notes.GetAsync(id, ct) is { } note ? Ok(note) : NotFound();

    [HttpPost]
    public async Task<ActionResult<NoteDto>> Create([FromBody] SaveNoteRequest request, CancellationToken ct)
    {
        var note = await notes.CreateAsync(request.Text ?? string.Empty, ct);
        return CreatedAtAction(nameof(Get), new { id = note.Id }, note);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<NoteDto>> Update(int id, [FromBody] SaveNoteRequest request, CancellationToken ct) =>
        await notes.UpdateAsync(id, request.Text ?? string.Empty, ct) is { } note ? Ok(note) : NotFound();

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await notes.DeleteAsync(id, ct) ? NoContent() : NotFound();

    /// <summary>Makes the note a task in the backlog and removes it; answers with the task.</summary>
    [HttpPost("{id:int}/to-backlog")]
    public async Task<ActionResult<BoardTaskDto>> MoveToBacklog(int id, CancellationToken ct) =>
        await notes.MoveToBacklogAsync(id, ct) is { } task ? Ok(task) : NotFound();
}
