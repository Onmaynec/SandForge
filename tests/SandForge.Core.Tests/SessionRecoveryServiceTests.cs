using System.Diagnostics;
using SandForge.Core;
using SandForge.Domain;
using Xunit;

namespace SandForge.Core.Tests;

public sealed class SessionRecoveryServiceTests
{
    [Fact]
    public async Task KeepsSessionRunningWhenRecordedProcessIsAlive()
    {
        string root = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SessionStore(root);
            var session = new SandboxSession
            {
                Id = "running", TemplateId = "minimal", CreatedAt = DateTimeOffset.UtcNow,
                Status = SessionStatus.Running, WorkspacePath = Path.Combine(root, "sessions", "running"),
                ConfigurationPath = "a.wsb", TargetFileHash = new string('D', 64), Risk = RiskLevel.Low,
                SandboxProcessId = Environment.ProcessId
            };
            await store.SaveAsync(session, CancellationToken.None);

            RecoveryResult result = await new SessionRecoveryService(store, new ArtifactManager()).RecoverAsync(CancellationToken.None);
            SandboxSession? loaded = await store.FindAsync(session.Id, CancellationToken.None);

            Assert.Equal(1, result.Inspected);
            Assert.Equal(0, result.Orphaned);
            Assert.NotNull(loaded);
            Assert.Equal(SessionStatus.Running, loaded.Status);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
