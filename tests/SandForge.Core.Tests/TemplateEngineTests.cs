using Xunit;
using SandForge.Core;
using SandForge.Domain;

namespace SandForge.Core.Tests;

public sealed class TemplateEngineTests
{
    [Fact]
    public async Task ParsesMinimalTemplate()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, """
        schemaVersion: 1
        metadata:
          name: minimal
          displayName: Minimal
          description: Safe template
        sandbox:
          network: disabled
          clipboard: disabled
          memoryMb: 2048
        session:
          timeout: 5m
        target:
          executable: "C:\\Sandbox\\Input\\${targetFileName}"
        artifacts:
          collectors:
            - user-output
        """);
        try
        {
            TemplateDefinition template = await new TemplateEngine().LoadAsync(path, CancellationToken.None);
            Assert.Equal("minimal", template.Metadata.Name);
            Assert.Equal(NetworkPolicy.Disabled, template.Sandbox.Network);
            Assert.Equal(TimeSpan.FromMinutes(5), template.Session.Timeout);
        }
        finally { File.Delete(path); }
    }
}
