using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Api.Infrastructure;
using PersonaOS.Application.Board;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// The sprint board. Tasks are addressed by id here; their <c>TASK-n</c> keys are for people and
/// the assistant. A scope change to a running sprint is refused with code
/// <c>scope_change_unacknowledged</c> until the request sets <c>acknowledgeScopeChange</c>.
/// </summary>
[ApiController]
[Route("api/board")]
[Authorize]
[RequireFeature(InstanceConfig.Modules.Board)]
public class BoardController(IBoardService board) : ControllerBase
{
    /// <summary>The board for <c>current</c> (default) or <c>next</c> sprint.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? sprint, CancellationToken ct) =>
        Ok(await board.GetBoardAsync(sprint ?? SprintViews.Current, ct));

    /// <summary>Past sprints with committed and completed points, plus velocity.</summary>
    [HttpGet("sprints")]
    public async Task<IActionResult> Report([FromQuery] int? count, CancellationToken ct) =>
        Ok(await board.GetReportAsync(count ?? 12, ct));

    /// <summary>Starts the planned sprint now, during the Sunday planning window.</summary>
    [HttpPost("sprints/start")]
    public async Task<IActionResult> Start(CancellationToken ct) =>
        Ok(await board.StartSprintAsync(ct));

    [HttpGet("tasks/{id:int}")]
    public async Task<IActionResult> GetTask(int id, CancellationToken ct)
    {
        var task = await board.GetTaskAsync(id, ct);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpPost("tasks")]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest request, CancellationToken ct)
    {
        var task = await board.CreateTaskAsync(request, ct);
        return CreatedAtAction(nameof(GetTask), new { id = task.Id }, task);
    }

    [HttpPut("tasks/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTaskRequest request, CancellationToken ct)
    {
        var task = await board.UpdateTaskAsync(id, request, ct);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpPut("tasks/{id:int}/move")]
    public async Task<IActionResult> Move(int id, [FromBody] MoveTaskRequest request, CancellationToken ct)
    {
        var task = await board.MoveTaskAsync(id, request, ct);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpDelete("tasks/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await board.DeleteTaskAsync(id, ct) ? NoContent() : NotFound();
}
