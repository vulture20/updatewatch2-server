using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.UpdateFilters;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Admin-facing CRUD for the global update filter list (CLAUDE.md "Key
/// configurable behaviors to preserve") — cookie-session gated, like every
/// other admin route. Applying a filter to the pending-updates display
/// itself happens in <see cref="Agents.AgentService"/>/<see cref="Updates.UpdateService"/>,
/// not here; this controller only manages the list.
/// </summary>
[ApiController]
[Route("api/admin/update-filters")]
[Authorize]
public class UpdateFiltersController(IUpdateFilterService filterService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await filterService.GetAllAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertUpdateFilterRequest request, CancellationToken ct)
    {
        var result = await filterService.CreateAsync(request, createdBy: User.Identity!.Name!, ct);
        return result.Success ? Ok(result.Filter) : BadRequest(new { message = result.FailureReason });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertUpdateFilterRequest request, CancellationToken ct)
    {
        var result = await filterService.UpdateAsync(id, request, updatedBy: User.Identity!.Name!, ct);
        if (result.Success)
        {
            return Ok(result.Filter);
        }

        return result.FailureReason == "Not found."
            ? NotFound()
            : BadRequest(new { message = result.FailureReason });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await filterService.DeleteAsync(id, deletedBy: User.Identity!.Name!, ct);
        return deleted ? NoContent() : NotFound();
    }
}
