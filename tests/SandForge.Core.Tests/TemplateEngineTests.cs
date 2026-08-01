using SandForge.Core;
using SandForge.Domain;
using Xunit;

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

    [Fact]
    public async Task ExtendsBaseAndMergesCollectors()
    {
        string root = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string basePath = Path.Combine(root, "base.yaml");
        string childPath = Path.Combine(root, "child.yaml");
        try
        {
            await File.WriteAllTextAsync(basePath, """
            schemaVersion: 2
            sandbox:
              network: disabled
              memoryMb: 2048
            artifacts:
              collectors:
                - user-output
            """);
            await File.WriteAllTextAsync(childPath, """
            schemaVersion: 2
            extends: "base.yaml"
            metadata:
              name: child
              displayName: Child
            sandbox:
              memoryMb: 4096
            artifacts:
              collectors:
                - process-list
            """);

            TemplateDefinition template = await new TemplateEngine().LoadAsync(childPath, CancellationToken.None);

            Assert.Equal("child", template.Metadata.Name);
            Assert.Equal(4096, template.Sandbox.MemoryMb);
            Assert.Contains("user-output", template.ArtifactCollectors);
            Assert.Contains("process-list", template.ArtifactCollectors);
            Assert.Equal(2, template.Sources.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task BlocksIncludeTraversalOutsideTemplateRoot()
    {
        string parent = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        string root = Path.Combine(parent, "templates");
        Directory.CreateDirectory(root);
        string outside = Path.Combine(parent, "outside.yaml");
        string template = Path.Combine(root, "sandforge.yaml");
        try
        {
            await File.WriteAllTextAsync(outside, "schemaVersion: 2");
            await File.WriteAllTextAsync(template, """
            schemaVersion: 2
            extends: "../outside.yaml"
            metadata:
              name: blocked
            """);

            await Assert.ThrowsAsync<InvalidDataException>(() => new TemplateEngine().LoadAsync(template, CancellationToken.None));
        }
        finally { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task ParsesProvisioningAndCache()
    {
        string root = Path.Combine(Path.GetTempPath(), "sandforge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string templatePath = Path.Combine(root, "sandforge.yaml");
        try
        {
            await File.WriteAllTextAsync(templatePath, """
            schemaVersion: 2
            metadata:
              name: provisioned
            sandbox:
              network: enabled
            provisioning:
              failurePolicy: continue
              packages:
                - id: Git.Git
                  version: "2.50.0"
                  source: winget
              installers:
                - path: "tool.exe"
                  timeout: 30s
                  arguments:
                    - "/quiet"
            cache:
              enabled: true
              maximumSizeMb: 512
              types:
                - nuget
            """);

            TemplateDefinition template = await new TemplateEngine().LoadAsync(templatePath, CancellationToken.None);

            Assert.Single(template.Provisioning.Packages);
            Assert.Single(template.Provisioning.Installers);
            Assert.Equal(ProvisioningFailurePolicy.Continue, template.Provisioning.FailurePolicy);
            Assert.True(template.Cache.Enabled);
            Assert.Equal(512, template.Cache.MaximumSizeMb);
            Assert.Contains("nuget", template.Cache.Types);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
