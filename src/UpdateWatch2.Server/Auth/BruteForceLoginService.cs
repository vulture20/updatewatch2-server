using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;

namespace UpdateWatch2.Server.Auth;

/// <summary>
/// In-memory brute-force tracker, keyed by username. Sufficient for a
/// single server instance; if the server is ever scaled out, this needs to
/// move to a shared store (e.g. a DB table) instead.
/// </summary>
public class BruteForceLoginService(IOptionsMonitor<BruteForceOptions> options) : IBruteForceLoginService
{
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _failedAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lockedUntil = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLockedOut(string username, IPAddress? remoteIp)
    {
        if (TrustedIpMatcher.IsTrusted(options.CurrentValue.TrustedIpRange, remoteIp))
        {
            return false;
        }

        return _lockedUntil.TryGetValue(username, out var until) && until > DateTimeOffset.UtcNow;
    }

    public void RecordFailedAttempt(string username, IPAddress? remoteIp)
    {
        if (TrustedIpMatcher.IsTrusted(options.CurrentValue.TrustedIpRange, remoteIp))
        {
            return;
        }

        var opts = options.CurrentValue;
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - TimeSpan.FromMinutes(opts.WindowMinutes);

        var attempts = _failedAttempts.GetOrAdd(username, _ => []);
        lock (attempts)
        {
            attempts.RemoveAll(t => t < windowStart);
            attempts.Add(now);

            if (attempts.Count >= opts.MaxAttempts)
            {
                _lockedUntil[username] = now + TimeSpan.FromMinutes(opts.LockoutMinutes);
                attempts.Clear();
            }
        }
    }

    public void RecordSuccessfulLogin(string username, IPAddress? remoteIp)
    {
        _failedAttempts.TryRemove(username, out _);
        _lockedUntil.TryRemove(username, out _);
    }
}
