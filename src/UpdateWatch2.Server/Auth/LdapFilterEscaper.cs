using System.Text;

namespace UpdateWatch2.Server.Auth;

/// <summary>
/// Escapes RFC 4515 special characters so a value from outside the app
/// (a submitted username, a directory-returned DN reused in a follow-up
/// filter) can't inject LDAP filter syntax — the LDAP-filter equivalent of
/// parameterizing a SQL query.
/// </summary>
public static class LdapFilterEscaper
{
    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append(@"\5c"); break;
                case '*': builder.Append(@"\2a"); break;
                case '(': builder.Append(@"\28"); break;
                case ')': builder.Append(@"\29"); break;
                case '\0': builder.Append(@"\00"); break;
                default: builder.Append(c); break;
            }
        }
        return builder.ToString();
    }
}
