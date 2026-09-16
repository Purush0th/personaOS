using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Api.Infrastructure;
using PersonaOS.Application.Board;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// The sprint board. Tasks and sprints are addressed by key (TASK-7, SPRINT-2), which is what the
/// user sees and what the web app puts in its URLs. A scope change to a running sprint is refused
/// with code <c>scope_change_unacknowledged</c> until the request sets <c>acknowledgeScopeChange</c>.
/// </summary>
[ApiController]
[Route("api/board")]
[Authorize]
[RequireFeature(InstanceConfig.Modules.Board)]
public class BoardController(IBoardService board) : ControllerBase
{
    /// <summary>The running sprint's columns. Empty when no sprint is running.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await board.GetBoardAsync(ct));

    /// <summary>The plan page: running sprint, sprints to come, then the backlog.</summary>
    [HttpGet("plan")]
    public async Task<IActionResult> Plan(CancellationToken ct) => Ok(await board.GetPlanAsync(ct));

    /// <summary>Past sprints with committed and completed points, plus velocity.</summary>
    [HttpGet("sprints")]
    public async Task<IActionResult> Report([FromQuery] int? count, CancellationToken ct) =>
        Ok(await board.GetReportAsync(count ?? 12, ct));

    [HttpGet("sprints/{key}")]
    public async Task<IActionResult> GetSprint(string key, CancellationToken ct)
    {
        var id = await board.ResolveSprintKeyAsync(key, ct);
        if (id is null) return NotFound();
        var sprint = await board.GetSprintAsync(id.Value, ct);
        return sprint is null ? NotFound() : Ok(sprint);
    }

    [HttpPost("sprints")]
    public async Task<IActionResult> CreateSprint([FromBody] CreateSprintRequest request, CancellationToken ct)
    {
        var sprint = await board.CreateSprintAsync(request, ct);
        return CreatedAtAction(nameof(GetSprint), new { key = sprint.Key }, sprint);
    }

    [HttpPut("sprints/{key}")]
    public async Task<IActionResult> UpdateSprint(string key, [FromBody] UpdateSprintRequest request, CancellationToken ct)
    {
        var id = await board.ResolveSprintKeyAsync(key, ct);
        if (id is null) return NotFound();
        var sprint = await board.UpdateSprintAsync(id.Value, request, ct);
        return sprint is null ? NotFound() : Ok(sprint);
    }

    /// <summary>Starts a planned sprint; only one runs at a time.</summary>
    [HttpPost("sprints/{key}/start")]
    public async Task<IActionResult> StartSprint(string key, CancellationToken ct)
    {
        var id = await board.ResolveSprintKeyAsync(key, ct);
        if (id is null) return NotFound();
        var sprint = await board.StartSprintAsync(id.Value, ct);
        return sprint is null ? NotFound() : Ok(sprint);
    }

    /// <summary>Completes the running sprint and moves unfinished work on.</summary>
    [HttpPost("sprints/{key}/complete")]
    public async Task<IActionResult> CompleteSprint(
        string key, [FromBody] CompleteSprintRequest? request, CancellationToken ct)
    {
        var id = await board.ResolveSprintKeyAsync(key, ct);
        if (id is null) return NotFound();
        var sprint = await board.CompleteSprintAsync(id.Value, request ?? new CompleteSprintRequest(), ct);
        return sprint is null ? NotFound() : Ok(sprint);
    }

    [HttpDelete("sprints/{key}")]
    public async Task<IActionResult> DeleteSprint(string key, CancellationToken ct)
    {
        var id = await board.ResolveSprintKeyAsync(key, ct);
        if (id is null) return NotFound();
        return await board.DeleteSprintAsync(id.Value, ct) ? NoContent() : NotFound();
    }

    /// <summary>One task in full: its fields, comments and attachments.</summary>
    [HttpGet("tasks/{key}")]
    public async Task<IActionResult> GetTask(string key, CancellationToken ct)
    {
        var id = await board.ResolveTaskKeyAsync(key, ct);
        if (id is null) return NotFound();
        var task = await board.GetTaskAsync(id.Value, ct);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpPost("tasks")]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest request, CancellationToken ct)
    {
        var task = await board.CreateTaskAsync(request, ct);
        return CreatedAtAction(nameof(GetTask), new { key = task.Key }, task);
    }

    [HttpPut("tasks/{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] UpdateTaskRequest request, CancellationToken ct)
    {
        var id = await board.ResolveTaskKeyAsync(key, ct);
        if (id is null) return NotFound();
        var task = await board.UpdateTaskAsync(id.Value, request, ct);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpPut("tasks/{key}/move")]
    public async Task<IActionResult> Move(string key, [FromBody] MoveTaskRequest request, CancellationToken ct)
    {
        var id = await board.ResolveTaskKeyAsync(key, ct);
        if (id is null) return NotFound();
        var task = await board.MoveTaskAsync(id.Value, request, ct);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpDelete("tasks/{key}")]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        var id = await board.ResolveTaskKeyAsync(key, ct);
        if (id is null) return NotFound();
        return await board.DeleteTaskAsync(id.Value, ct) ? NoContent() : NotFound();
    }
}
