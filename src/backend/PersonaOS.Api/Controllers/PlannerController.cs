using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Api.Infrastructure;
using PersonaOS.Application.Planner;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// The planner: events on days, which can repeat. Changing or deleting one of a repeating event
/// takes <c>occurrence</c> (the <c>occurrenceDate</c> of the item) and <c>scope</c>: <c>this</c>,
/// <c>following</c> or <c>all</c> (the default).
/// </summary>
[ApiController]
[Route("api/planner")]
[Authorize]
[RequireFeature(InstanceConfig.Modules.Planner)]
public class PlannerController(IPlannerService planner) : ControllerBase
{
    public record UpdateStatusRequest(string Status);
    public record MoveRequest(DateOnly Date);

    /// <summary>One day's plan (default today), or a range when from/to are supplied.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly? date,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        if (from is not null || to is not null)
        {
            if (from is null || to is null)
                return BadRequest(new { error = "Provide both 'from' and 'to'.", code = "planner_validation_failed" });
            return Ok(await planner.GetRangeAsync(from.Value, to.Value, ct));
        }

        return Ok(await planner.GetDayAsync(date ?? DateOnly.FromDateTime(DateTime.UtcNow), ct));
    }

    [HttpGet("items/{id:int}")]
    public async Task<IActionResult> GetItem(int id, [FromQuery] DateOnly? occurrence, CancellationToken ct)
    {
        var item = await planner.GetAsync(id, occurrence, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost("items")]
    public async Task<IActionResult> Create([FromBody] CreatePlannerItemRequest request, CancellationToken ct)
    {
        var item = await planner.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetItem), new { id = item.Id }, item);
    }

    [HttpPut("items/{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdatePlannerItemRequest request,
        [FromQuery] string? scope,
        [FromQuery] DateOnly? occurrence,
        CancellationToken ct)
    {
        if (!TryTarget(scope, occurrence, out var target)) return BadScope();
        var item = await planner.UpdateAsync(id, request, target, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPut("items/{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(
        int id, [FromBody] UpdateStatusRequest request, [FromQuery] DateOnly? occurrence, CancellationToken ct)
    {
        var item = await planner.UpdateStatusAsync(id, request.Status, occurrence, ct);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>Moves an item to another day (for a repeating one, just the occurrence unless the scope says more).</summary>
    [HttpPut("items/{id:int}/date")]
    public async Task<IActionResult> Move(
        int id,
        [FromBody] MoveRequest request,
        [FromQuery] string? scope,
        [FromQuery] DateOnly? occurrence,
        CancellationToken ct)
    {
        if (!TryTarget(scope, occurrence, out var target)) return BadScope();
        var item = await planner.MoveAsync(id, request.Date, target, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> Delete(
        int id, [FromQuery] string? scope, [FromQuery] DateOnly? occurrence, CancellationToken ct)
    {
        if (!TryTarget(scope, occurrence, out var target)) return BadScope();
        return await planner.DeleteAsync(id, target, ct) ? NoContent() : NotFound();
    }

    private static bool TryTarget(string? scope, DateOnly? occurrence, out PlannerTarget? target)
    {
        target = null;
        PlannerEditScope parsed;
        switch (scope?.Trim().ToLowerInvariant())
        {
            case null or "" or "all":
                parsed = PlannerEditScope.All;
                break;
            case "this":
                parsed = PlannerEditScope.This;
                break;
            case "following":
                parsed = PlannerEditScope.Following;
                break;
            default:
                return false;
        }

        target = new PlannerTarget(parsed, occurrence);
        return true;
    }

    private BadRequestObjectResult BadScope() => BadRequest(new
    {
        error = "scope must be 'this', 'following' or 'all'.",
        code = "planner_validation_failed",
    });
}
