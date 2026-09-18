using System.Text.Json.Serialization;

namespace UpdateWatch2.Server.Api;

/// <summary>
/// A stable, machine-readable identifier for an admin-facing API error,
/// carried alongside the existing free-text <c>message</c>/<c>errors</c>
/// fields rather than replacing them (updatewatch2-server#17 — "Admin-facing
/// API error messages are free-text English with no translation
/// mechanism"). This is what lets the web UI translate a failure via
/// react-i18next's <c>t()</c> instead of displaying hardcoded English
/// verbatim regardless of the admin's chosen language — the one class of
/// UI-visible text that couldn't be bilingual before this, per CLAUDE.md's
/// "the UI must stay bilingual" requirement.
///
/// Serialized as its name, not the default numeric encoding — the same
/// <see cref="JsonStringEnumConverter"/> convention every other enum-like
/// wire value in this codebase already uses (see e.g.
/// <c>Updates.InstallOutcome</c>'s own doc comment for the live-confirmed
/// reason: a bare number 400s against a hand-typed request body).
///
/// This list only ever grows — a value already shipped is never renamed or
/// removed, so an older frontend build talking to a newer server simply
/// falls back to the raw <c>message</c>/<c>errors[].message</c> text for a
/// code it doesn't recognize (<c>web/src/api/client.ts</c>'s
/// <c>translateErrorCode</c>), and a newer frontend talking to an older
/// server that never sends a code at all falls back exactly the same way.
///
/// Deliberately scoped to STATIC, human-authored failure reasons only —
/// not every error this API can ever produce. Two genuinely dynamic-content
/// cases exist (a live SMTP exception's own message; a regex engine's own
/// parse-error message for whatever pattern an admin just typed) — both
/// keep a code (<see cref="TestEmailFailed"/>/<see cref="UpdateFilterPatternInvalid"/>)
/// but carry the dynamic fragment in a separate, additive <c>errorDetail</c>
/// field for <c>{{detail}}</c>-style interpolation, the same pattern this
/// app's i18n already uses elsewhere (e.g. <c>agentDetail.deleteConfirm</c>'s
/// <c>{{hostname}}</c>) — translating an arbitrary upstream library's own
/// English text would be its own, out-of-scope project. A route that's
/// agent-facing rather than admin-facing (mutual-TLS, e.g.
/// <c>AgentProtocolController.Register</c>/<c>.Renew</c>) is out of scope
/// entirely — its failure text is read by the agent's own logs, never by a
/// browser, so there's nothing here for react-i18next to translate.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApiErrorCode
{
    // AdminController.Validate (PUT /api/admin/settings)
    LogLevelInvalid,
    BruteForceMaxAttemptsInvalid,
    BruteForceWindowMinutesInvalid,
    BruteForceLockoutMinutesInvalid,
    SmtpPortInvalid,
    SmtpEncryptionInvalid,
    NotificationUpdatesPerMachineThresholdInvalid,
    NotificationAffectedMachinesThresholdInvalid,
    AdEncryptionInvalid,
    AdPortInvalid,
    AdHostRequired,
    AdBaseDnRequired,
    AdUserSearchFilterRequired,
    AdUserSearchFilterMissingPlaceholder,
    AdLoginGroupDnRequired,
    AgentCertificateValidityDaysInvalid,
    AgentAutoUpdateCheckIntervalHoursInvalid,
    AuditLogRetentionDaysInvalid,
    CertificateExpiryWarningLeadDaysInvalid,
    NotificationRecipientAddressInvalid,
    InstanceUrlInvalid,
    AgentOfflineThresholdMinutesInvalid,

    // AuthController
    TooManyFailedAttempts,
    InvalidCredentials,
    AdSessionCannotChangePassword,
    PasswordChangeRejected,

    // NotificationsController
    TestEmailToAddressInvalid,
    SmtpNotConfigured,
    /// <summary>Dynamic — pairs with an <c>errorDetail</c> carrying the live SMTP exception's own message.</summary>
    TestEmailFailed,

    // AgentUpdatesController.Upload
    NoFilesUploaded,
    /// <summary>Dynamic — <c>errorDetail</c> carries the offending filename.</summary>
    UnrecognizedAssetFile,
    /// <summary>Dynamic — <c>errorDetail</c> carries the duplicated asset kind.</summary>
    DuplicateAssetKind,
    /// <summary>Dynamic — <c>errorDetail</c> carries the offending filename.</summary>
    VersionNotExtractable,
    /// <summary>Dynamic — <c>errorDetail</c> carries the two conflicting versions found.</summary>
    VersionMismatch,
    AutoUpdateMustBeEnabledForUpload,

    // AgentsController.ReissueCertificate
    AgentNotApproved,

    // UpdateFiltersController / UpdateFilterService.ValidateAsync
    UpdateFilterNameRequired,
    UpdateFilterPatternRequired,
    /// <summary>Dynamic — <c>errorDetail</c> carries the regex engine's own parse-error message.</summary>
    UpdateFilterPatternInvalid,
    UpdateFilterNameTaken,

    // CertificateAuthorityController
    NoRotationPendingToActivate,
    NoPreviousRootToRetire,

    // SchedulesController / Schedules.ScheduleService.ValidateAsync
    ScheduleNameRequired,
    ScheduleAgentsRequired,
    ScheduleActionRequired,
    ScheduleDeadlineHoursInvalid,
    ScheduleOnceAtRequired,
    ScheduleOnceAtInPast,
    SchedulePatternRequired,
    ScheduleWeeklyDaysRequired,
    ScheduleIntervalDaysInvalid,
    ScheduleIntervalStartDateRequired,
    /// <summary>Dynamic — <c>errorDetail</c> carries the unknown hostname(s).</summary>
    ScheduleUnknownAgents,
}
