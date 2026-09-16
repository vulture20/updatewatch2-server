using UpdateWatch2.Server.Admin;

namespace UpdateWatch2.Server.Tests.Admin;

public class AdminSettingsDiffFormatterTests
{
    private static AdminSettingsDto Baseline() => new(
        LogLevel: "INFO",
        BruteForceMaxAttempts: 6,
        BruteForceWindowMinutes: 5,
        BruteForceLockoutMinutes: 30,
        SmtpHost: "smtp.example.com",
        SmtpPort: 587,
        SmtpUsername: null,
        SmtpPasswordSet: false,
        SmtpEncryption: "StartTls",
        SmtpFromAddress: "updatewatch2@example.com",
        SmtpFromName: "UpdateWatch2",
        NotificationRecipientAddress: null,
        InstanceUrl: null,
        SmtpConfigured: true,
        NotificationUpdatesPerMachineThreshold: 5,
        NotificationUpdatesPerMachineEnabled: true,
        NotificationAffectedMachinesThreshold: 10,
        NotificationAffectedMachinesEnabled: true,
        AdEnabled: false,
        AdHost: "",
        AdPort: 389,
        AdEncryption: "StartTls",
        AdBindDn: "",
        AdBindPasswordSet: false,
        AdBaseDn: "",
        AdUserSearchFilter: "(&(objectClass=user)(sAMAccountName={0}))",
        AdLoginGroupDn: "",
        AdConfigured: false,
        AgentCertificateValidityDays: 730,
        AgentAutoUpdateEnabled: true,
        GitHubTokenSet: false,
        AgentAutoUpdateCheckIntervalHours: 6,
        AuditLogRetentionDays: 90,
        CertificateExpiryWarningLeadDays: 60,
        CertificateExpiryNotificationsEnabled: true,
        AgentOfflineThresholdMinutes: 15,
        AgentOfflineNotificationEnabled: true,
        AgentOnlineRecoveryNotificationEnabled: true);

    [Fact]
    public void Returns_null_when_nothing_changed()
    {
        var result = AdminSettingsDiffFormatter.Format(Baseline(), Baseline());

        Assert.Null(result);
    }

    [Fact]
    public void Reports_each_changed_field_with_its_before_and_after_value()
    {
        var after = Baseline() with { BruteForceMaxAttempts = 9, LogLevel = "DEBUG" };

        var result = AdminSettingsDiffFormatter.Format(Baseline(), after);

        Assert.Contains("BruteForceMaxAttempts: 6 -> 9", result);
        Assert.Contains("LogLevel: INFO -> DEBUG", result);
    }

    [Fact]
    public void Does_not_mention_a_field_that_did_not_change()
    {
        var after = Baseline() with { BruteForceMaxAttempts = 9 };

        var result = AdminSettingsDiffFormatter.Format(Baseline(), after);

        Assert.DoesNotContain("SmtpPort", result);
    }

    [Fact]
    public void Reports_a_password_being_set_only_as_the_boolean_flag_never_the_secret_itself()
    {
        // AdminSettingsDto never carries a raw password/token value in the
        // first place — this asserts the diff genuinely can't leak one,
        // not just that this particular call site avoids it.
        var after = Baseline() with { SmtpPasswordSet = true };

        var result = AdminSettingsDiffFormatter.Format(Baseline(), after);

        Assert.Equal("SmtpPasswordSet: False -> True", result);
    }
}
