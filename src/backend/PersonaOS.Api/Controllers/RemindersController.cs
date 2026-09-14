using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Api.Infrastructure;
using PersonaOS.Application.Reminders;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Controllers;

[ApiController]
[Route("api/reminders")]
[Authorize]
[RequireFeature(InstanceConfig.Modules.Reminders)]
public class RemindersController(IReminderService reminders) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeCompleted, CancellationToken ct) =>
        Ok(await reminders.ListAsync(includeCompleted, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var reminder = await reminders.GetAsync(id, ct);
        return reminder is null ? NotFound() : Ok(reminder);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReminderRequest request, CancellationToken ct)
    {
        var reminder = await reminders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = reminder.Id }, reminder);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateReminderRequest request, CancellationToken ct)
    {
        var reminder = await reminders.UpdateAsync(id, request, ct);
        return reminder is null ? NotFound() : Ok(reminder);
    }

    /// <summary>Cancels a pending reminder so it will not fire.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var reminder = await reminders.CancelAsync(id, ct);
        return reminder is null ? NotFound() : Ok(reminder);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await reminders.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
