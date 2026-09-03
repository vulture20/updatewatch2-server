using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UpdateWatch2.Server.Auth;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;

namespace UpdateWatch2.Server.Tests.TestHelpers;

/// <summary>
/// Seeds a known admin account directly via the DbContext (bypassing
/// AdminAccountService.EnsureSeededAsync's random password, which no test
/// could otherwise know) and logs in through the real endpoint, so tests
/// exercise the same auth path the frontend does.
/// </summary>
public static class AuthTestHelper
{
    public const string Username = "admin";
    public const string Password = "Sup3r$ecretTestPassw0rd!";

    public static async Task SeedAdminAsync(IServiceProvider services, string username = Username, string password = Password)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = new PasswordHasher<AdminAccount>();

        // The app's own startup seeding (Program.cs -> IAdminAccountService.EnsureSeededAsync)
        // already runs by the time this executes — it creates the account
        // with an unknown random password. Overwrite its hash with the
        // known test password rather than skipping when a row exists.
        var account = await db.AdminAccounts.SingleOrDefaultAsync(a => a.Username == username);
        if (account is null)
        {
            account = new AdminAccount { Username = username, PasswordHash = "" };
            db.AdminAccounts.Add(account);
        }

        account.PasswordHash = hasher.HashPassword(account, password);
        await db.SaveChangesAsync();
    }

    public static async Task LoginAsync(HttpClient client, string username = Username, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        response.EnsureSuccessStatusCode();
    }
}
