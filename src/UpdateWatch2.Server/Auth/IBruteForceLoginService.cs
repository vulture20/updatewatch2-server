using System.Net;

namespace UpdateWatch2.Server.Auth;

public interface IBruteForceLoginService
{
    /// <summary>True if the given username is currently locked out (and the caller's IP isn't trusted).</summary>
    bool IsLockedOut(string username, IPAddress? remoteIp);

    void RecordFailedAttempt(string username, IPAddress? remoteIp);

    void RecordSuccessfulLogin(string username, IPAddress? remoteIp);
}
