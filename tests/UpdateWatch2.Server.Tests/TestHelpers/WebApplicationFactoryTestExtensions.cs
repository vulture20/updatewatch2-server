using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace UpdateWatch2.Server.Tests.TestHelpers;

public static class WebApplicationFactoryTestExtensions
{
    /// <summary>
    /// Strips every registered <see cref="IHostedService"/> — as of this
    /// writing, just <c>AgentUpdateCheckWorker</c> (updatewatch2-server#14)
    /// — from a <c>WebApplicationFactory</c>-backed test host. Without
    /// this, every <c>WebApplicationFactory&lt;Program&gt;</c>-based
    /// integration test in this project starts that worker for real,
    /// which makes a genuine outbound HTTPS call to the live GitHub API
    /// and, whenever a version change is detected, writes real
    /// multi-megabyte downloaded files into the working directory —
    /// caught by hand, not by a failing test: a routine <c>dotnet test</c>
    /// run in this project's own dev sandbox left two real files (~25 MB
    /// and ~6 MB) sitting under <c>server/agent-updates/</c> afterward.
    /// Every existing <c>WebApplicationFactory&lt;Program&gt;</c>-based
    /// test class in this project is an <c>IClassFixture</c>, so this
    /// only needs applying once per test class's <c>WithWebHostBuilder</c>
    /// call, not per test method.
    /// </summary>
    public static IWebHostBuilder WithoutBackgroundWorkers(this IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
}
