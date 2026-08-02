using SandForge.Domain;
using SandForge.Reporting;
using Xunit;

namespace SandForge.Core.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void EveryLanguageContainsAllRequiredKeys(string language)
    {
        Assert.Empty(UiText.MissingKeys(language));
    }

    [Fact]
    public void EnglishReportUsesEnglishLabelsAndPreservesLanguageInJson()
    {
        var session = new SandboxSession
        {
            Id = "session-1",
            TemplateId = "minimal",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = SessionStatus.Completed,
            WorkspacePath = "workspace",
            ConfigurationPath = "sandbox.wsb",
            TargetFileHash = new string('A', 64),
            Risk = RiskLevel.Low,
            Cleanup = CleanupState.Cleaned
        };
        var writer = new ReportWriter(UiText.English);

        string console = writer.ToConsole(session);

        Assert.Contains("SANDFORGE SESSION REPORT", console, StringComparison.Ordinal);
        Assert.Contains("Completed", console, StringComparison.Ordinal);
        Assert.DoesNotContain("ОТЧЁТ", console, StringComparison.Ordinal);
    }
}
