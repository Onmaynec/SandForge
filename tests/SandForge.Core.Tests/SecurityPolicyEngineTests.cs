using Xunit;
using SandForge.Core;
using SandForge.Domain;

namespace SandForge.Core.Tests;

public sealed class SecurityPolicyEngineTests
{
    [Fact]
    public async Task DefaultTemplate_IsLowRisk()
    {
        var template = new TemplateDefinition
        {
            Metadata = new("minimal", "Minimal", "test"),
            Sandbox = new(),
            Session = new(),
            Target = new()
        };
        var engine = new SecurityPolicyEngine();
        SecurityEvaluationResult result = await engine.EvaluateAsync(template, @"C:\Temp\app.exe", CancellationToken.None);
        Assert.Equal(RiskLevel.Low, result.Risk);
        Assert.False(result.IsBlocked);
    }

    [Fact]
    public async Task WritableSystemDrive_IsBlocked()
    {
        var template = new TemplateDefinition
        {
            Metadata = new("danger", "Danger", "test"),
            Mounts = [new(@"C:\", @"C:\Host", MountMode.ReadWrite)],
            Target = new()
        };
        var engine = new SecurityPolicyEngine();
        SecurityEvaluationResult result = await engine.EvaluateAsync(template, @"C:\Temp\app.exe", CancellationToken.None);
        Assert.True(result.IsBlocked);
        Assert.Equal(RiskLevel.Critical, result.Risk);
    }

    [Fact]
    public async Task PackageProvisioningWithoutNetwork_IsBlocked()
    {
        var template = new TemplateDefinition
        {
            Metadata = new("provisioned", "Provisioned", "test"),
            Sandbox = new SandboxSettings { Network = NetworkPolicy.Disabled },
            Provisioning = new ProvisioningSettings
            {
                Packages = [new PackageDefinition { Id = "Git.Git", Version = "2.50.0" }]
            },
            Target = new()
        };
        SecurityEvaluationResult result = await new SecurityPolicyEngine().EvaluateAsync(template, @"C:\Temp\app.exe", CancellationToken.None);
        Assert.True(result.IsBlocked);
        Assert.Contains(result.Findings, x => x.Code == "PROVISIONING_NETWORK_DISABLED");
    }
}
