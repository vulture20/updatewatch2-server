using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Sockets;
using UpdateWatch2.Server.Admin;

namespace UpdateWatch2.Server.Auth;

public class ActiveDirectoryAuthService(IAdminSettingsStore settingsStore, ILogger<ActiveDirectoryAuthService> logger)
    : IActiveDirectoryAuthService
{
    // System.DirectoryServices.Protocols is fully synchronous — there's no
    // async LDAP API to await, so the whole operation runs on a pool
    // thread. LDAP operations are normally fast (local network, indexed
    // search); ct is honored between steps, not mid-request.
    public Task<AdAuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default) =>
        Task.Run(() => Authenticate(username, password, ct), ct);

    private AdAuthResult Authenticate(string username, string password, CancellationToken ct)
    {
        var ad = settingsStore.Ad;
        if (!ad.Enabled || !ad.IsConfigured)
        {
            return new AdAuthResult(false, null, "AD login is not configured.");
        }

        LdapConnection searchConnection;
        try
        {
            searchConnection = CreateConnection(ad);
            searchConnection.Bind(new NetworkCredential(ad.BindDn, ad.BindPassword));
        }
        catch (Exception ex) when (ex is LdapException or SocketException)
        {
            logger.LogWarning(ex, "AD service-account bind failed against {Host}:{Port}", ad.Host, ad.Port);
            return new AdAuthResult(false, null, "Could not connect to the directory server.");
        }

        using (searchConnection)
        {
            ct.ThrowIfCancellationRequested();

            var escapedUsername = LdapFilterEscaper.Escape(username);
            var filter = string.Format(ad.UserSearchFilter, escapedUsername);

            SearchResponse userResponse;
            try
            {
                var userRequest = new SearchRequest(ad.BaseDn, filter, SearchScope.Subtree, "cn");
                userResponse = (SearchResponse)searchConnection.SendRequest(userRequest);
            }
            catch (DirectoryOperationException ex)
            {
                logger.LogWarning(ex, "AD user search failed for base DN {BaseDn}", ad.BaseDn);
                return new AdAuthResult(false, null, "User search failed.");
            }

            if (userResponse.Entries.Count == 0)
            {
                return new AdAuthResult(false, null, "Invalid username or password.");
            }

            if (userResponse.Entries.Count > 1)
            {
                logger.LogWarning("AD user search for {Username} matched {Count} entries — refusing to authenticate an ambiguous match.", username, userResponse.Entries.Count);
                return new AdAuthResult(false, null, "Invalid username or password.");
            }

            var entry = userResponse.Entries[0];
            var userDn = entry.DistinguishedName;
            var displayName = entry.Attributes.Contains("cn") ? entry.Attributes["cn"][0] as string : username;

            ct.ThrowIfCancellationRequested();

            // The actual password check: binding as the user IS how LDAP
            // verifies a password — there's no separate "verify" call.
            try
            {
                using var authConnection = CreateConnection(ad);
                authConnection.Bind(new NetworkCredential(userDn, password));
                authConnection.Dispose();
            }
            catch (LdapException)
            {
                return new AdAuthResult(false, null, "Invalid username or password.");
            }

            ct.ThrowIfCancellationRequested();

            try
            {
                var membershipFilter = $"(member={LdapFilterEscaper.Escape(userDn)})";
                var membershipRequest = new SearchRequest(ad.LoginGroupDn, membershipFilter, SearchScope.Base);
                var membershipResponse = (SearchResponse)searchConnection.SendRequest(membershipRequest);

                if (membershipResponse.Entries.Count == 0)
                {
                    return new AdAuthResult(false, null, "User is not a member of the configured login group.");
                }
            }
            catch (DirectoryOperationException ex)
            {
                logger.LogWarning(ex, "AD login-group membership check failed for group DN {GroupDn}", ad.LoginGroupDn);
                return new AdAuthResult(false, null, "Login group not found.");
            }

            return new AdAuthResult(true, displayName ?? username, null);
        }
    }

    private static LdapConnection CreateConnection(AdOptions ad)
    {
        var connection = new LdapConnection(new LdapDirectoryIdentifier(ad.Host, ad.Port))
        {
            AuthType = AuthType.Basic,
        };
        connection.SessionOptions.ProtocolVersion = 3;

        if (ad.Encryption == AdEncryption.Ldaps)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }
        else if (ad.Encryption == AdEncryption.StartTls)
        {
            connection.SessionOptions.StartTransportLayerSecurity(null);
        }

        return connection;
    }
}
