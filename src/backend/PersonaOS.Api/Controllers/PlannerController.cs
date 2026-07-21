using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Api.Infrastructure;
using PersonaOS.Application.Planner;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Controllers;

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
    public async Task<IActionResult> GetItem(int id, CancellationToken ct)
    {
        var item = await planner.GetAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost("items")]
    public async Task<IActionResult> Create([FromBody] CreatePlannerItemRequest request, CancellationToken ct)
    {
        var item = await planner.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetItem), new { id = item.Id }, item);
    }

    [HttpPut("items/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePlannerItemRequest request, CancellationToken ct)
    {
        var item = await planner.UpdateAsync(id, request, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPut("items/{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        var item = await planner.UpdateStatusAsync(id, request.Status, ct);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>Moves an item to another day.</summary>
    [HttpPut("items/{id:int}/date")]
    public async Task<IActionResult> Move(int id, [FromBody] MoveRequest request, CancellationToken ct)
    {
        var item = await planner.MoveAsync(id, request.Date, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await planner.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
