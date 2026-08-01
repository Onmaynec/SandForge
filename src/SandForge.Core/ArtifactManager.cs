using System.Security.Cryptography;
using System.Text.Json;
using SandForge.Domain;

namespace SandForge.Core;

public sealed class ArtifactManager : IArtifactManager
{
    private const long MaximumArtifactBytes = 256L * 1024L * 1024L;
    private const int MaximumArtifacts = 10_000;

    public async Task<IReadOnlyList<SessionArtifact>> ImportAsync(
        SessionWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var artifacts = new List<SessionArtifact>();
        if (!Directory.Exists(workspace.Output)) return artifacts;

        foreach (string source in Directory.EnumerateFiles(workspace.Output, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (artifacts.Count >= MaximumArtifacts) break;
            string relative = Path.GetRelativePath(workspace.Output, source);
            if (relative.StartsWith(".sandforge", StringComparison.OrdinalIgnoreCase)) continue;

            FileInfo info = new(source);
            if (info.Length > MaximumArtifactBytes) continue;

            string destination = Path.GetFullPath(Path.Combine(workspace.Artifacts, relative));
            string artifactRoot = Path.GetFullPath(workspace.Artifacts).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(artifactRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Artifact path traversal detected.");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
            string hash = await ComputeSha256Async(destination, cancellationToken);
            artifacts.Add(new SessionArtifact
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = "user-output",
                RelativePath = relative.Replace('\\', '/'),
                Size = info.Length,
                Sha256 = hash,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        string manifestPath = Path.Combine(workspace.Metadata, "artifacts.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(artifacts, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return artifacts;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
