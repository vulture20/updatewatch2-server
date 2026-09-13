using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.AgentUpdates;
using UpdateWatch2.Server.Audit;

namespace UpdateWatch2.Server.Api.Controllers;

/// <summary>
/// Status plus an admin-triggered manual check for the admin UI's
/// agent-auto-update section (updatewatch2-server#14) — the enabled/token
/// toggle itself lives on the existing <c>PUT /api/admin/settings</c> (see
/// <see cref="AdminController"/>), same as every other admin setting; this
/// is just the additional, non-editable "what's the newest version this
/// server currently knows about" state that setting doesn't carry, plus a
/// way to force a check right now instead of waiting for
/// <see cref="AgentUpdateCheckWorker"/>'s own interval.
/// </summary>
[ApiController]
[Route("api/admin/agent-update-status")]
[Authorize]
public class AgentUpdatesController(IAgentUpdateService agentUpdateService, IAuditLogService auditLog) : ControllerBase
{
    // 200 MB — comfortably above all three current platform assets
    // combined (~95 MB today) with headroom for future growth. Must be a
    // class-level const, not a local one inside Upload, since it's
    // referenced from that method's own attributes below (attribute
    // arguments are resolved independently of the method body).
    private const long MaxUploadBytes = 200_000_000;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await agentUpdateService.GetStatusAsync(ct));

    /// <summary>
    /// Runs the exact same check <see cref="AgentUpdateCheckWorker"/> runs
    /// on its own interval — <see cref="IAgentUpdateService.CheckForUpdatesAsync"/>
    /// is documented as safe to call more often than that interval for
    /// this exact reason (a no-op read whenever nothing changed on
    /// GitHub), so this needs no extra guarding beyond the normal admin
    /// auth this whole controller already requires. Audit-logged with the
    /// outcome so "an admin forced a check and what it found" is
    /// distinguishable from the periodic worker's own
    /// <c>agent-update.detected</c>/<c>agent-update.assets-redownloaded</c>
    /// entries.
    /// </summary>
    [HttpPost("check")]
    public async Task<IActionResult> Check(CancellationToken ct)
    {
        var outcome = await agentUpdateService.CheckForUpdatesAsync(ct);
        await auditLog.LogAsync(User.Identity!.Name!, "agent-update.manual-check", outcome.ToString(), ct);
        return Ok(await agentUpdateService.GetStatusAsync(ct));
    }

    /// <summary>
    /// The escape hatch for a server that deliberately has no internet
    /// access (CLAUDE.md's "Agent auto-update" bullet) — an admin uploads
    /// the release assets (any of <c>.exe</c>/<c>.deb</c>/<c>.rpm</c>,
    /// one of each at most) directly instead of this server ever needing
    /// to reach GitHub. Feeds the exact same <c>AgentUpdateState</c> row
    /// <see cref="AgentUpdateCheckWorker"/>'s GitHub downloads do, so the
    /// result is offered to agents identically either way.
    ///
    /// The release version is extracted from the uploaded filenames
    /// (<see cref="AgentUpdateVersionExtractor"/>) rather than taken as a
    /// separate form field — the original design had an admin type it in
    /// by hand alongside the file picker, which a typo or a mismatched
    /// file could silently get wrong; every filename must agree on the
    /// same version or the whole request is rejected.
    ///
    /// <see cref="MaxUploadBytes"/> raises both Kestrel's default
    /// <c>MaxRequestBodySize</c> (30 MB) and ASP.NET Core's default
    /// <c>MultipartBodyLengthLimit</c> (128 MB) — this project's own real
    /// release assets already run close to the former on their own (see
    /// CLAUDE.md's note on the ~33 MB Windows installer), so the
    /// unmodified defaults would silently reject a normal upload well
    /// within what a real release actually needs, the same class of
    /// "framework default doesn't match this project's real payload size"
    /// trap <c>Certs:Path</c>/the csproj <c>&lt;Version&gt;</c> element
    /// already document elsewhere in this codebase.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Upload(IFormFileCollection files, CancellationToken ct)
    {
        // Every Detail here (updatewatch2-server#17) carries exactly the
        // bit of admin-supplied context the un-translated Message already
        // interpolates — a filename, an asset kind, a version pair — the
        // same {{placeholder}} interpolation this app's i18n already uses
        // elsewhere, not the "arbitrary opaque exception text" case
        // ApiErrorCode's own doc comment scopes Detail's other use to.
        var errors = new List<ApiErrorItem>();
        string? version = null;

        if (files.Count == 0)
        {
            errors.Add(new ApiErrorItem(ApiErrorCode.NoFilesUploaded, "At least one file must be uploaded."));
        }
        else
        {
            var seenKinds = new HashSet<AgentUpdateAssetKind>();
            foreach (var file in files)
            {
                var kind = AgentUpdateAssetClassifier.Classify(file.FileName);
                if (kind is null)
                {
                    errors.Add(new ApiErrorItem(ApiErrorCode.UnrecognizedAssetFile, $"'{file.FileName}' is not a recognized agent release asset (.exe, .deb, or .rpm).", file.FileName));
                    continue;
                }

                if (!seenKinds.Add(kind.Value))
                {
                    errors.Add(new ApiErrorItem(ApiErrorCode.DuplicateAssetKind, $"More than one {kind} file was uploaded in the same request.", kind.Value.ToString()));
                    continue;
                }

                var extractedVersion = AgentUpdateVersionExtractor.Extract(file.FileName);
                if (extractedVersion is null)
                {
                    errors.Add(new ApiErrorItem(ApiErrorCode.VersionNotExtractable, $"Could not determine the release version from '{file.FileName}' — expected a filename containing the version, e.g. 'updatewatch2-agent_0.13.0_amd64.deb'.", file.FileName));
                }
                else if (version is null)
                {
                    version = extractedVersion;
                }
                else if (!string.Equals(version, extractedVersion, StringComparison.Ordinal))
                {
                    errors.Add(new ApiErrorItem(ApiErrorCode.VersionMismatch, $"All uploaded files must be from the same release version (found {version} and {extractedVersion}).", $"{version}, {extractedVersion}"));
                }
            }
        }

        if (errors.Count > 0)
        {
            return BadRequest(new { errors });
        }

        // version is guaranteed non-null here: files.Count > 0 (else the
        // "at least one file" error above), and every file in the loop
        // either added an error (short-circuiting to the 400 above) or
        // set/confirmed a single consistent version.
        var uploaded = files.Select(f => new UploadedAgentAsset(f.FileName, f.OpenReadStream())).ToList();
        try
        {
            var outcome = await agentUpdateService.UploadAssetsAsync(version!, uploaded, ct);
            if (outcome == AgentUpdateUploadOutcome.Disabled)
            {
                return BadRequest(new { errors = new[] { new ApiErrorItem(ApiErrorCode.AutoUpdateMustBeEnabledForUpload, "Agent auto-update must be enabled to accept a manual upload.") } });
            }
        }
        finally
        {
            foreach (var file in uploaded)
            {
                await file.Content.DisposeAsync();
            }
        }

        await auditLog.LogAsync(User.Identity!.Name!, "agent-update.manual-upload", $"{version} ({string.Join(", ", files.Select(f => f.FileName))})", ct);
        return Ok(await agentUpdateService.GetStatusAsync(ct));
    }
}
