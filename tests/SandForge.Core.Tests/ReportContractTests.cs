using System.Text.Json;
using SandForge.Core;
using SandForge.Domain;
using SandForge.Reporting;
using Xunit;

namespace SandForge.Core.Tests;

public sealed class ReportContractTests
{
    [Fact]
    public async Task JsonReportUsesVersionedStableEnvelope()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sandforge-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "report.json");
        try
        {
            var writer = new ReportWriter(UiText.English, "0.5.0-alpha-test");
            var session = new SandboxSession
            {
                Id = "session-contract-test",
                TemplateId = "minimal",
                CreatedAt = DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
                Status = SessionStatus.Completed,
                WorkspacePath = "workspace",
                ConfigurationPath = "sandbox.wsb",
                TargetFileHash = new string('A', 64),
                Risk = RiskLevel.Low
            };

            await writer.WriteJsonAsync(session, path, CancellationToken.None);
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            JsonElement root = document.RootElement;

            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("en", root.GetProperty("language").GetString());
            Assert.Equal("0.5.0-alpha-test", root.GetProperty("generatorVersion").GetString());
            Assert.Equal("completed", root.GetProperty("session").GetProperty("status").GetString());

            var service = new CompatibilityService(new TemplateEngine());
            ContractValidationResult result = await service.ValidateAsync(path, "report", CancellationToken.None);
            Assert.True(result.IsValid);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
