using Xunit;

// Disable xUnit's default parallel-across-test-classes execution.
//
// Found necessary by a real CI-only failure, not by inspection: several
// tests (e.g. AdminAccountServiceTests, AgentUpdateServiceTests) set a
// process-wide environment variable via Environment.SetEnvironmentVariable
// to exercise env-var-driven behavior — a pattern that's fine in isolation,
// but AdminAccountServiceTests' UPDATEWATCH2_RESET_ADMIN_PASSWORD is also
// read unconditionally by AdminAccountService.ResetPasswordFromEnvironmentIfConfiguredAsync
// on every real Program.cs startup, which every WebApplicationFactory<Program>-based
// Api/ test triggers. With classes running in parallel (xUnit's default),
// a real host build could observe that variable set mid-flight by a
// concurrently-running AdminAccountServiceTests test, silently resetting
// the admin account WebApplicationFactory<Program>-based tests had just
// seeded a known password for — NotificationsControllerTests hit exactly
// this in CI (a 401 on a login using the known test password) despite a
// full local `dotnet test` run passing clean, since local timing didn't
// happen to overlap. Matches the class of gap this project's own agent
// repo already documented once for WorkerTests (a CI-only race a fast
// local run doesn't reproduce) — same lesson, different repo.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
