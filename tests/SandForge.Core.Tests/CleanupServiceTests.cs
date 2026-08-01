using Xunit;
using SandForge.Core;
using SandForge.Domain;

namespace SandForge.Core.Tests;

public sealed class CleanupServiceTests
{
    [Fact]
    public async Task DryRunDoesNotDeleteWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        string workspace = Path.Combine(root, "sessions", "old"); Directory.CreateDirectory(workspace); await File.WriteAllTextAsync(Path.Combine(workspace, "a.txt"), "data");
        try
        {
            var store = new SessionStore(root);
            await store.SaveAsync(new SandboxSession { Id="old", TemplateId="minimal", CreatedAt=DateTimeOffset.UtcNow.AddDays(-40), EndedAt=DateTimeOffset.UtcNow.AddDays(-40), Status=SessionStatus.Completed, WorkspacePath=workspace, ConfigurationPath="a.wsb", TargetFileHash=new string('B',64), Risk=RiskLevel.Low }, CancellationToken.None);
            CleanupResult result = await new CleanupService(store).CleanupAsync(TimeSpan.FromDays(30), false, true, CancellationToken.None);
            Assert.Single(result.Candidates); Assert.True(Directory.Exists(workspace)); Assert.Equal(0, result.CleanedCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task IgnoresWorkspaceOutsideSandForgeSessionsDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "sandforge-outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            var store = new SessionStore(root);
            await store.SaveAsync(new SandboxSession
            {
                Id = "outside", TemplateId = "minimal", CreatedAt = DateTimeOffset.UtcNow.AddDays(-40),
                EndedAt = DateTimeOffset.UtcNow.AddDays(-40), Status = SessionStatus.Completed,
                WorkspacePath = outside, ConfigurationPath = "a.wsb", TargetFileHash = new string('C', 64), Risk = RiskLevel.Low
            }, CancellationToken.None);
            CleanupResult result = await new CleanupService(store).CleanupAsync(TimeSpan.FromDays(30), false, false, CancellationToken.None);
            Assert.Empty(result.Candidates);
            Assert.True(Directory.Exists(outside));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

}
