using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using UpdateWatch2.Server.Tests.TestHelpers;
using UpdateWatch2.Server.UpdateFilters;

namespace UpdateWatch2.Server.Tests.Api;

public class UpdateFiltersControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"updatewatch2-test-{Guid.NewGuid()}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;
    private HttpClient _anonymousClient = null!;

    public UpdateFiltersControllerTests(WebApplicationFactory<Program> factory)
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
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _anonymousClient.Dispose();
        File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetAll_requires_an_admin_session()
    {
        var response = await _anonymousClient.GetAsync("/api/admin/update-filters");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_requires_an_admin_session()
    {
        var response = await _anonymousClient.PostAsJsonAsync("/api/admin/update-filters", new UpsertUpdateFilterRequest("x", "y"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_requires_an_admin_session()
    {
        var response = await _anonymousClient.PutAsJsonAsync("/api/admin/update-filters/1", new UpsertUpdateFilterRequest("x", "y"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_requires_an_admin_session()
    {
        var response = await _anonymousClient.DeleteAsync("/api/admin/update-filters/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_includes_the_default_Defender_filter_seeded_at_startup()
    {
        var response = await _client.GetAsync("/api/admin/update-filters");

        response.EnsureSuccessStatusCode();
        var filters = await response.Content.ReadFromJsonAsync<List<UpdateFilterDto>>();
        Assert.Contains(filters!, f => f.Name == UpdateFilterService.DefaultFilterName);
    }

    [Fact]
    public async Task Create_then_update_then_delete_round_trips_through_the_API()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/admin/update-filters", new UpsertUpdateFilterRequest("Edge", "Microsoft Edge"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<UpdateFilterDto>();
        Assert.Equal("Edge", created!.Name);

        var updateResponse = await _client.PutAsJsonAsync($"/api/admin/update-filters/{created.Id}", new UpsertUpdateFilterRequest("Edge (renamed)", "Edge Update"));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<UpdateFilterDto>();
        Assert.Equal("Edge (renamed)", updated!.Name);

        var deleteResponse = await _client.DeleteAsync($"/api/admin/update-filters/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await _client.GetAsync("/api/admin/update-filters");
        var filters = await listResponse.Content.ReadFromJsonAsync<List<UpdateFilterDto>>();
        Assert.DoesNotContain(filters!, f => f.Id == created.Id);
    }

    [Fact]
    public async Task Create_returns_400_for_an_invalid_regex()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/update-filters", new UpsertUpdateFilterRequest("Broken", "("));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_returns_404_for_an_unknown_id()
    {
        var response = await _client.PutAsJsonAsync("/api/admin/update-filters/999999", new UpsertUpdateFilterRequest("x", "y"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_returns_404_for_an_unknown_id()
    {
        var response = await _client.DeleteAsync("/api/admin/update-filters/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
