using Xunit;
using SandForge.Core;
using SandForge.Domain;

namespace SandForge.Core.Tests;

public sealed class SessionStoreTests
{
    [Fact]
    public async Task SavesAndLoadsSessionWithCollectors()
    {
        string root = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SessionStore(root);
            var session = new SandboxSession
            {
                Id = "test-session", TemplateId = "installer-test", CreatedAt = DateTimeOffset.UtcNow,
                Status = SessionStatus.Completed, WorkspacePath = Path.Combine(root, "sessions", "test-session"),
                ConfigurationPath = "test.wsb", TargetFileHash = new string('A', 64), Risk = RiskLevel.Low,
                Collectors = [new CollectorResult { Id = "services", RelativePath = "collectors/services-diff.json", ItemCount = 2 }]
            };
            await store.SaveAsync(session, CancellationToken.None);
            SandboxSession? loaded = await store.FindAsync(session.Id, CancellationToken.None);
            Assert.NotNull(loaded); Assert.Equal(SessionStatus.Completed, loaded.Status); Assert.Single(loaded.Collectors); Assert.True(File.Exists(store.DatabasePath));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
