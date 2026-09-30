using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonaOS.Api.Infrastructure;
using PersonaOS.Application.Configuration;
using PersonaOS.Application.Memories;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Controllers;

/// <summary>
/// What the assistant remembers across conversations, for the Memories screen: view, add, edit,
/// delete, and the auto-save switch. The assistant itself goes through the memory tools.
/// </summary>
[ApiController]
[Route("api/memories")]
[Authorize]
[RequireFeature(InstanceConfig.Modules.Memory)]
public class MemoriesController(IMemoryService memories, IInstanceConfigService configService) : ControllerBase
{
    public record SaveMemoryRequest(string? Content, string? Category);

    public record MemorySettings(bool AutoSave);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MemoryDto>>> List([FromQuery] string? search, [FromQuery] string? category, CancellationToken ct) =>
        Ok(await memories.ListAsync(search, category, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MemoryDto>> Get(int id, CancellationToken ct) =>
        await memories.GetAsync(id, ct) is { } memory ? Ok(memory) : NotFound();

    [HttpPost]
    public async Task<ActionResult<MemoryDto>> Create([FromBody] SaveMemoryRequest request, CancellationToken ct)
    {
        var memory = await memories.CreateAsync(request.Content ?? string.Empty, request.Category, null, ct);
        return CreatedAtAction(nameof(Get), new { id = memory.Id }, memory);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MemoryDto>> Update(int id, [FromBody] SaveMemoryRequest request, CancellationToken ct)
    {
        if (await memories.GetAsync(id, ct) is null) return NotFound();
        return Ok(await memories.UpdateAsync(id, request.Content, request.Category, ct));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await memories.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [HttpGet("settings")]
    public async Task<ActionResult<MemorySettings>> GetSettings(CancellationToken ct) =>
        Ok(new MemorySettings((await configService.GetOrCreateAsync(ct)).MemoryAutoSave));

    /// <summary>Auto-save on: the assistant saves memories and shows a receipt. Off: each one is a card first.</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<MemorySettings>> UpdateSettings([FromBody] MemorySettings request, CancellationToken ct)
    {
        await configService.UpdateAsync(c => c.MemoryAutoSave = request.AutoSave, ct);
        return Ok(request);
    }
}
