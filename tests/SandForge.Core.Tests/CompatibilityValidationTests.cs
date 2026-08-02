using SandForge.Core;
using SandForge.Domain;
using Xunit;

namespace SandForge.Core.Tests;

public sealed class CompatibilityValidationTests
{
    [Fact]
    public async Task DeprecatedTemplateIsAcceptedWithWarning()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "sandforge.yaml");
        await File.WriteAllTextAsync(path, "schemaVersion: 1\nmetadata:\n  name: legacy-template\n");
        try
        {
            var service = new CompatibilityService(new TemplateEngine());
            ContractValidationResult result = await service.ValidateAsync(path, "template", CancellationToken.None);

            Assert.True(result.IsValid);
            Assert.Equal(1, result.DetectedVersion);
            Assert.Single(result.Warnings);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ConfigRejectsUnknownLanguage()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "sandforge.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":2,\"storage\":{},\"session\":{},\"security\":{},\"cache\":{},\"updates\":{},\"privacy\":{},\"ui\":{\"language\":\"de\"}}");
        try
        {
            var service = new CompatibilityService(new TemplateEngine());
            ContractValidationResult result = await service.ValidateAsync(path, null, CancellationToken.None);

            Assert.False(result.IsValid);
            Assert.Equal("config", result.ContractId);
            Assert.Contains("ui.language must be ru, en or auto.", result.Errors);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string NewDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "sandforge-contract-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
