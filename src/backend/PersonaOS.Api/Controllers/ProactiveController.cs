using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Api.Infrastructure;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Proactive;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Api.Controllers;

[ApiController]
[Route("api/proactive")]
[Authorize]
[RequireFeature(InstanceConfig.Modules.Proactive)]
public class ProactiveController(
    IProactiveService proactive,
    IAppDbContext db) : ControllerBase
{
    /// <summary>Recent proactive runs, newest first — what was sent and when.</summary>
    [HttpGet("runs")]
    public async Task<IActionResult> Runs(CancellationToken ct) =>
        Ok(await db.ProactiveJobRuns.AsNoTracking()
            .OrderByDescending(r => r.RanAtUtc)
            .Take(30)
            .Select(r => new { r.Id, r.JobName, r.LocalDate, r.RanAtUtc, r.Pushed, r.Summary })
            .ToListAsync(ct));

    /// <summary>
    /// Runs a job now, ignoring its schedule. Useful for previewing a brief and
    /// for verification. Pass force=false to respect the once-per-day rule.
    /// </summary>
    [HttpPost("run/{jobName}")]
    public async Task<IActionResult> Run(string jobName, [FromQuery] bool force = true, CancellationToken ct = default)
    {
        if (!ProactiveJobs.All.Contains(jobName))
            return BadRequest(new
            {
                error = $"Unknown job '{jobName}'. Valid jobs: {string.Join(", ", ProactiveJobs.All)}.",
                code = "unknown_proactive_job",
            });

        return Ok(await proactive.RunJobAsync(jobName, force, ct));
    }
}
