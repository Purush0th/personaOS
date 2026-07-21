using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Api.Infrastructure;
using PersonaOS.Application.Goals;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Controllers;

[ApiController]
[Route("api/goals")]
[Authorize]
[RequireFeature(InstanceConfig.Modules.Goals)]
public class GoalsController(IGoalService goals) : ControllerBase
{
    public record UpdateStatusRequest(string Status);
    public record LinkRequest(int? ParentGoalId);

    /// <summary>Goal hierarchy (roots with nested children) incl. rollup progress.</summary>
    [HttpGet]
    public async Task<IActionResult> GetTree([FromQuery] bool includeDropped, CancellationToken ct) =>
        Ok(await goals.GetTreeAsync(includeDropped, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var goal = await goals.GetAsync(id, ct);
        return goal is null ? NotFound() : Ok(goal);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGoalRequest request, CancellationToken ct)
    {
        var goal = await goals.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = goal.Id }, goal);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateGoalRequest request, CancellationToken ct)
    {
        var goal = await goals.UpdateAsync(id, request, ct);
        return goal is null ? NotFound() : Ok(goal);
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        var goal = await goals.UpdateStatusAsync(id, request.Status, ct);
        return goal is null ? NotFound() : Ok(goal);
    }

    /// <summary>Re-parents a goal; null parentGoalId makes it top-level.</summary>
    [HttpPut("{id:int}/parent")]
    public async Task<IActionResult> Link(int id, [FromBody] LinkRequest request, CancellationToken ct)
    {
        var goal = await goals.LinkAsync(id, request.ParentGoalId, ct);
        return goal is null ? NotFound() : Ok(goal);
    }

    /// <summary>Deletes a goal and its whole subtree.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await goals.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
