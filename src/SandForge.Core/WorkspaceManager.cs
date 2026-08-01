using System.Text.Json;
using SandForge.Domain;

namespace SandForge.Core;

public sealed class WorkspaceManager : IWorkspaceManager
{
    public async Task<SessionWorkspace> PrepareAsync(
        SessionPlan plan,
        string dataDirectory,
        CancellationToken cancellationToken)
    {
        string sessionsRoot = Path.Combine(Path.GetFullPath(dataDirectory), "sessions");
        Directory.CreateDirectory(sessionsRoot);
        string root = Path.Combine(sessionsRoot, plan.SessionId);
        EnsureInside(sessionsRoot, root);

        var workspace = new SessionWorkspace
        {
            Root = root,
            Input = Path.Combine(root, "input"),
            Output = Path.Combine(root, "output"),
            Bootstrap = Path.Combine(root, "bootstrap"),
            Config = Path.Combine(root, "config"),
            Artifacts = Path.Combine(root, "artifacts"),
            Logs = Path.Combine(root, "logs"),
            Metadata = Path.Combine(root, "metadata")
        };

        foreach (string directory in new[] { workspace.Root, workspace.Input, workspace.Output, workspace.Bootstrap, workspace.Config, workspace.Artifacts, workspace.Logs, workspace.Metadata })
            Directory.CreateDirectory(directory);

        string destination = Path.Combine(workspace.Input, plan.TargetFileName);
        await CopyAsync(plan.TargetSourcePath, destination, cancellationToken);

        if (plan.Installers.Count > 0)
        {
            string provisioningDirectory = Path.Combine(workspace.Input, "provisioning");
            Directory.CreateDirectory(provisioningDirectory);
            foreach (ProvisioningInstallerPlan installer in plan.Installers)
            {
                string installerDestination = Path.Combine(provisioningDirectory, Path.GetFileName(installer.GuestPath));
                await CopyAsync(installer.SourcePath, installerDestination, cancellationToken);
            }
        }

        string planPath = Path.Combine(workspace.Metadata, "plan.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        return workspace;
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static void EnsureInside(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.GetFullPath(candidate);
        if (!normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Workspace path escaped the SandForge data directory.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
