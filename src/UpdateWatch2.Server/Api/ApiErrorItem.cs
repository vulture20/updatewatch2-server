namespace UpdateWatch2.Server.Api;

/// <summary>
/// One entry of a multi-error <c>{ "errors": [...] }</c> response (form
/// validation, e.g. <c>AdminController.Validate</c>) — replaces what used
/// to be a bare <c>string[]</c>, pairing every message with the stable
/// <see cref="ApiErrorCode"/> the web UI translates via <c>t()</c>
/// (updatewatch2-server#17). <see cref="Message"/> is the exact English
/// text this API always returned before this existed — kept byte-identical
/// as the fallback for a frontend build that doesn't recognize
/// <see cref="Code"/> yet, so this is purely additive on the wire, not a
/// replacement. See <see cref="ApiErrorCode"/>'s own doc comment for what
/// <see cref="Detail"/> is for and why only two codes ever set it.
/// </summary>
public record ApiErrorItem(ApiErrorCode Code, string Message, string? Detail = null);
