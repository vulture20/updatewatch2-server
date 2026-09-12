using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using UpdateWatch2.Server.Api;
using UpdateWatch2.Server.Api.Controllers;

namespace UpdateWatch2.Server.Tests.Api;

/// <summary>
/// A metadata audit, not an integration test — <c>WebApplicationFactory</c>'s
/// in-memory <c>TestServer</c> doesn't bind real ports, so
/// <c>Program.cs</c>'s port-gating middleware (which keys off
/// <c>HttpContext.Connection.LocalPort</c>) can't be exercised the same
/// way this project's other mTLS-adjacent behavior already can't be —
/// same standing limitation, see <c>ApiEndpointTests</c>' own remarks on
/// it. What real regression value this test class CAN provide instead:
/// asserting the exact set of routes <see cref="AllowedOnAgentPortAttribute"/>
/// is attached to, both positively (the agent-facing ones a real agent
/// genuinely calls over the agent port) and negatively (the admin-facing
/// controllers that must stay blocked there) — a live run against a real
/// two-port server is still what's needed to prove the middleware itself
/// actually enforces this, not just that the metadata is attached where
/// intended.
/// </summary>
public class AllowedOnAgentPortAttributeTests
{
    private static bool IsAllowed(MethodInfo action) =>
        action.GetCustomAttribute<AllowedOnAgentPortAttribute>() is not null
        || action.DeclaringType!.GetCustomAttribute<AllowedOnAgentPortAttribute>() is not null;

    [Theory]
    [InlineData(typeof(AgentProtocolController), nameof(AgentProtocolController.Register))]
    [InlineData(typeof(AgentProtocolController), nameof(AgentProtocolController.Alive))]
    [InlineData(typeof(AgentProtocolController), nameof(AgentProtocolController.Renew))]
    [InlineData(typeof(AgentProtocolController), nameof(AgentProtocolController.AcknowledgeReboot))]
    [InlineData(typeof(AgentProtocolController), nameof(AgentProtocolController.CaCertificate))]
    [InlineData(typeof(AgentProtocolController), nameof(AgentProtocolController.CaCertificateBundle))]
    [InlineData(typeof(AgentProtocolController), nameof(AgentProtocolController.DownloadUpdate))]
    [InlineData(typeof(VersionController), nameof(VersionController.Get))]
    [InlineData(typeof(UpdatesController), nameof(UpdatesController.ReportUpdates))]
    [InlineData(typeof(UpdatesController), nameof(UpdatesController.AcknowledgeInstall))]
    public void Agent_facing_actions_a_real_agent_calls_over_the_agent_port_are_allowed(Type controller, string actionName)
    {
        var action = controller.GetMethod(actionName) ?? throw new InvalidOperationException($"{controller.Name}.{actionName} not found.");
        Assert.True(IsAllowed(action), $"{controller.Name}.{actionName} should carry [AllowedOnAgentPort].");
    }

    // The specific bug this whole attribute exists to fix: these two
    // controllers' cookie-session-gated admin routes must stay blocked on
    // the agent port, even though nothing about ASP.NET Core's own
    // routing would otherwise separate them from the agent-facing ones
    // above sharing the identical "api/agents/{hostname}/..." prefix.
    [Theory]
    [InlineData(typeof(AgentsController), nameof(AgentsController.GetAll))]
    [InlineData(typeof(AgentsController), nameof(AgentsController.GetByHostname))]
    [InlineData(typeof(AgentsController), nameof(AgentsController.Approve))]
    [InlineData(typeof(AgentsController), nameof(AgentsController.Reboot))]
    [InlineData(typeof(AgentsController), nameof(AgentsController.Delete))]
    [InlineData(typeof(UpdatesController), nameof(UpdatesController.GetUpdates))]
    [InlineData(typeof(UpdatesController), nameof(UpdatesController.TriggerInstall))]
    public void Admin_facing_actions_are_not_allowed_on_the_agent_port(Type controller, string actionName)
    {
        var action = controller.GetMethod(actionName) ?? throw new InvalidOperationException($"{controller.Name}.{actionName} not found.");
        Assert.False(IsAllowed(action), $"{controller.Name}.{actionName} must NOT carry [AllowedOnAgentPort] — it's an admin-only, cookie-gated route.");
    }

    // Every controller in the whole API surface, not just a hand-picked
    // few — catches a future admin-facing controller that forgets it's
    // implicitly relying on this gate the moment a new one is added,
    // rather than only the ones this test class happened to name above.
    [Fact]
    public void No_admin_only_controller_is_marked_AllowedOnAgentPort()
    {
        var adminOnlyControllers = new[]
        {
            typeof(AgentsController), typeof(AdminController), typeof(AuthController),
            typeof(HealthController), typeof(CertificateAuthorityController),
            typeof(AgentUpdatesController), typeof(UpdateFiltersController), typeof(NotificationsController),
        };

        foreach (var controller in adminOnlyControllers)
        {
            Assert.Null(controller.GetCustomAttribute<AllowedOnAgentPortAttribute>());
        }
    }
}
