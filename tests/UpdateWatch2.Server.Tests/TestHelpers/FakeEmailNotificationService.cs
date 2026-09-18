using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Tests.TestHelpers;

/// <summary>
/// A minimal hand-written IEmailNotificationService fake, promoted from a
/// private nested class in UpdateThresholdNotificationWorkerTests once a
/// second and third test class (ScheduleServiceTests, UpdateServiceTests/
/// AgentServiceTests' schedule-failure-notification tests) needed the exact
/// same shape — records every SendNotificationAsync call for assertion, and
/// can be told to throw to exercise the "audit-logged only if actually
/// sent, retried whole on the next attempt" pattern several automated
/// notifications in this codebase share.
/// </summary>
public class FakeEmailNotificationService : IEmailNotificationService
{
    public List<(string To, string Subject, string Body)> SentNotifications { get; } = [];

    public int SendAttemptCount { get; private set; }

    public bool ThrowOnSend { get; set; }

    public Task SendTestEmailAsync(string toAddress, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<bool> IsHealthyAsync(CancellationToken ct = default) => throw new NotSupportedException();

    public Task SendNotificationAsync(string toAddress, string subjectEn, string bodyEn, string subjectDe, string bodyDe, CancellationToken ct = default)
    {
        SendAttemptCount++;
        if (ThrowOnSend)
        {
            throw new InvalidOperationException("simulated SMTP failure");
        }

        SentNotifications.Add((toAddress, subjectEn, bodyEn));
        return Task.CompletedTask;
    }
}
