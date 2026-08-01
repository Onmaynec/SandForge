using System.Security.Cryptography;
using System.Text.Json;
using SandForge.Domain;

namespace SandForge.Core;

public sealed class ArtifactManager : IArtifactManager
{
    private const long MaximumArtifactBytes = 256L * 1024L * 1024L;
    private const int MaximumArtifacts = 10_000;

    public async Task<ArtifactImportResult> ImportAsync(SessionWorkspace workspace, CancellationToken cancellationToken)
    {
        var artifacts = new List<SessionArtifact>();
        var collectors = new List<CollectorResult>();
        Directory.CreateDirectory(workspace.Artifacts);

        if (Directory.Exists(workspace.Output))
        {
            foreach (string source in Directory.EnumerateFiles(workspace.Output, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (artifacts.Count >= MaximumArtifacts) break;
                string relative = Path.GetRelativePath(workspace.Output, source).Replace('\\', '/');
                if (relative.Equals(".sandforge/completed.json", StringComparison.OrdinalIgnoreCase) ||
                    relative.Equals(".sandforge/bootstrap-error.txt", StringComparison.OrdinalIgnoreCase)) continue;

                string type = relative.StartsWith(".sandforge/collectors/", StringComparison.OrdinalIgnoreCase)
                    ? "collector"
                    : "user-output";
                string publicRelative = type == "collector" ? relative[".sandforge/".Length..] : relative;
                SessionArtifact? artifact = await ImportFileAsync(source, publicRelative, type, workspace.Artifacts, cancellationToken);
                if (artifact is null) continue;
                artifacts.Add(artifact);
                if (type == "collector") collectors.Add(await InspectCollectorAsync(source, publicRelative, cancellationToken));
            }
        }

        string manifestPath = Path.Combine(workspace.Metadata, "artifacts.json");
        Directory.CreateDirectory(workspace.Metadata);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { artifacts, collectors }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return new ArtifactImportResult(artifacts, collectors);
    }

    private static async Task<SessionArtifact?> ImportFileAsync(string source, string relative, string type, string artifactDirectory, CancellationToken cancellationToken)
    {
        FileInfo info = new(source);
        if (info.Length > MaximumArtifactBytes) return null;
        string destination = Path.GetFullPath(Path.Combine(artifactDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
        string root = Path.GetFullPath(artifactDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Обнаружена попытка выхода пути артефакта за пределы каталога.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        return new SessionArtifact
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            RelativePath = relative,
            Size = info.Length,
            Sha256 = await ComputeSha256Async(destination, cancellationToken),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static async Task<CollectorResult> InspectCollectorAsync(string path, string relative, CancellationToken cancellationToken)
    {
        string id = Path.GetFileNameWithoutExtension(path).Replace("-diff", string.Empty, StringComparison.OrdinalIgnoreCase);
        try
        {
            await using FileStream stream = File.OpenRead(path);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            int count = root.ValueKind switch
            {
                JsonValueKind.Array => root.GetArrayLength(),
                JsonValueKind.Object when root.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array => items.GetArrayLength(),
                _ => 0
            };
            string? error = root.ValueKind == JsonValueKind.Object &&
                            root.TryGetProperty("error", out JsonElement errorElement) &&
                            errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            return new CollectorResult { Id = id, RelativePath = relative, ItemCount = count, Error = error };
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return new CollectorResult { Id = id, RelativePath = relative, Error = exception.Message };
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
