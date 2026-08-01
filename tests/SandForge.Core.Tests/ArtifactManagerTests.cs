using System.Text.Json;
using SandForge.Core;
using SandForge.Domain;
using Xunit;

namespace SandForge.Core.Tests;

public sealed class ArtifactManagerTests
{
    [Fact]
    public async Task ImportsCollectorPayloadAndPreservesError()
    {
        string root = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        SessionWorkspace workspace = SessionWorkspace.FromRoot(root);
        Directory.CreateDirectory(Path.Combine(workspace.Output, ".sandforge", "collectors"));
        try
        {
            string path = Path.Combine(workspace.Output, ".sandforge", "collectors", "services-diff.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                collector = "services",
                items = new[] { new { change = "Added", key = "svc" } },
                error = "частичный сбор"
            }));

            ArtifactImportResult result = await new ArtifactManager().ImportAsync(workspace, CancellationToken.None);

            CollectorResult collector = Assert.Single(result.Collectors);
            Assert.Equal("services", collector.Id);
            Assert.Equal(1, collector.ItemCount);
            Assert.Equal("частичный сбор", collector.Error);
            Assert.Single(result.Artifacts);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
