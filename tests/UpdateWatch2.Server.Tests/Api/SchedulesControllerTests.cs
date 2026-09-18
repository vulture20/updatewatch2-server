using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UpdateWatch2.Server.Db;
using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.Schedules;
using UpdateWatch2.Server.Tests.TestHelpers;

namespace UpdateWatch2.Server.Tests.Api;

public class SchedulesControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;
    private HttpClient _anonymousClient = null!;

    public SchedulesControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.WithoutBackgroundWorkers().ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Path"] = _dbPath,
                })));
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _anonymousClient = _factory.CreateClient();
        await AuthTestHelper.SeedAdminAsync(_factory.Services);
        await AuthTestHelper.LoginAsync(_client);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Agents.Add(new Agent { Hostname = "schedules-test-host", Approved = true });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _anonymousClient.Dispose();
        File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    private static UpsertScheduleRequest ValidRequest(string name = "Nightly install") => new(
        Name: name,
        Enabled: true,
        ScheduleType: ScheduleType.Once,
        Pattern: null,
        OnceAt: DateTimeOffset.UtcNow.AddDays(1),
        WeeklyDays: null,
        TimeOfDay: default,
        IntervalDays: null,
        IntervalStartDate: null,
        CronExpression: null,
        ActionInstall: true,
        ActionReboot: false,
        RebootOnlyIfRequired: false,
        DeadlineHours: 4,
        NotifyOnFailure: true,
        Hostnames: ["schedules-test-host"]);

    [Fact]
    public async Task GetAll_requires_an_admin_session()
    {
        var response = await _anonymousClient.GetAsync("/api/schedules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_requires_an_admin_session()
    {
        var response = await _anonymousClient.PostAsJsonAsync("/api/schedules", ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_serializes_enum_fields_as_strings_not_numbers()
    {
        var response = await _client.PostAsJsonAsync("/api/schedules", ValidRequest());
        response.EnsureSuccessStatusCode();

        // Verbatim JSON, not the deserialized DTO — the whole point is to
        // catch System.Text.Json's default numeric enum encoding, which a
        // round-trip through the same strongly-typed record on both ends
        // would never expose (see AgentUpdateOfferBackwardCompatibilityTests'
        // server-side precedent for exactly this class of gap).
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"scheduleType\":\"Once\"", json);
        Assert.Contains("\"status\":\"Active\"", json);
    }

    [Fact]
    public async Task Create_then_get_then_update_then_delete_round_trips_through_the_API()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/schedules", ValidRequest());
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ScheduleDto>();
        Assert.Equal("Nightly install", created!.Name);
        Assert.Equal(["schedules-test-host"], created.Hostnames);

        var getResponse = await _client.GetAsync($"/api/schedules/{created.Id}");
        getResponse.EnsureSuccessStatusCode();

        var updateResponse = await _client.PutAsJsonAsync($"/api/schedules/{created.Id}", ValidRequest("Nightly install (renamed)"));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<ScheduleDto>();
        Assert.Equal("Nightly install (renamed)", updated!.Name);

        var deleteResponse = await _client.DeleteAsync($"/api/schedules/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await _client.GetAsync("/api/schedules");
        var schedules = await listResponse.Content.ReadFromJsonAsync<List<ScheduleDto>>();
        Assert.DoesNotContain(schedules!, s => s.Id == created.Id);
    }

    [Fact]
    public async Task Create_returns_400_with_a_stable_error_code_for_no_agents_selected()
    {
        var response = await _client.PostAsJsonAsync("/api/schedules", ValidRequest() with { Hostnames = [] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ScheduleAgentsRequired", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Update_returns_404_for_an_unknown_id()
    {
        var response = await _client.PutAsJsonAsync("/api/schedules/999999", ValidRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_returns_404_for_an_unknown_id()
    {
        var response = await _client.DeleteAsync("/api/schedules/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RunNow_triggers_an_install_and_shows_up_in_the_run_history()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/schedules", ValidRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<ScheduleDto>();

        var runNowResponse = await _client.PostAsync($"/api/schedules/{created!.Id}/run-now", null);
        Assert.Equal(HttpStatusCode.NoContent, runNowResponse.StatusCode);

        var runsResponse = await _client.GetAsync($"/api/schedules/{created.Id}/runs");
        runsResponse.EnsureSuccessStatusCode();
        var runs = await runsResponse.Content.ReadFromJsonAsync<List<ScheduleRunDto>>();
        Assert.Single(runs!);
        Assert.Equal("schedules-test-host", runs![0].Agents.Single().Hostname);
    }

    [Fact]
    public async Task RunNow_returns_404_for_an_unknown_id()
    {
        var response = await _client.PostAsync("/api/schedules/999999/run-now", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
