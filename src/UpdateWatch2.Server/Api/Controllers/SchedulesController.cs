using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Schedules;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Admin-facing CRUD and run history for zeitgesteuerte Wartungsfenster
/// (updatewatch2-server#25) — cookie-session gated, like every other admin
/// route. A top-level resource (<c>api/schedules</c>, not <c>api/admin/...</c>),
/// matching <c>AgentsController</c>'s own convention for operational
/// (rather than settings-page) data.
/// </summary>
[ApiController]
[Route("api/schedules")]
[Authorize]
public class SchedulesController(IScheduleService scheduleService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await scheduleService.GetAllAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var schedule = await scheduleService.GetByIdAsync(id, ct);
        return schedule is null ? NotFound() : Ok(schedule);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertScheduleRequest request, CancellationToken ct)
    {
        var result = await scheduleService.CreateAsync(request, createdBy: User.Identity!.Name!, ct);
        return result.Success ? Ok(result.Schedule) : BadRequest(new { message = result.FailureReason, errorCode = result.ErrorCode, errorDetail = result.ErrorDetail });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertScheduleRequest request, CancellationToken ct)
    {
        var result = await scheduleService.UpdateAsync(id, request, updatedBy: User.Identity!.Name!, ct);
        if (result.Success)
        {
            return Ok(result.Schedule);
        }

        return result.FailureReason == "Not found."
            ? NotFound()
            : BadRequest(new { message = result.FailureReason, errorCode = result.ErrorCode, errorDetail = result.ErrorDetail });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await scheduleService.DeleteAsync(id, deletedBy: User.Identity!.Name!, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/run-now")]
    public async Task<IActionResult> RunNow(int id, CancellationToken ct)
    {
        var triggered = await scheduleService.RunNowAsync(id, triggeredBy: User.Identity!.Name!, ct);
        return triggered ? NoContent() : NotFound();
    }

    [HttpGet("{id:int}/runs")]
    public async Task<IActionResult> GetRuns(int id, CancellationToken ct)
    {
        var schedule = await scheduleService.GetByIdAsync(id, ct);
        if (schedule is null)
        {
            return NotFound();
        }

        return Ok(await scheduleService.GetRunsAsync(id, ct));
    }
}
