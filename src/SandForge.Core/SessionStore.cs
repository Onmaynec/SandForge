using System.Text.Json;
using SandForge.Domain;

namespace SandForge.Core;

public sealed class SessionStore(string dataDirectory)
{
    private readonly string _indexPath = Path.Combine(Path.GetFullPath(dataDirectory), "sessions", "index.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task SaveAsync(SandboxSession session, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
        List<SandboxSession> sessions = (await LoadAsync(cancellationToken)).ToList();
        int index = sessions.FindIndex(x => x.Id == session.Id);
        if (index >= 0) sessions[index] = session; else sessions.Add(session);
        string temp = _indexPath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(sessions.OrderByDescending(x => x.CreatedAt), Options), cancellationToken);
        File.Move(temp, _indexPath, true);
    }

    public async Task<IReadOnlyList<SandboxSession>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_indexPath)) return Array.Empty<SandboxSession>();
        await using FileStream stream = File.OpenRead(_indexPath);
        return await JsonSerializer.DeserializeAsync<List<SandboxSession>>(stream, Options, cancellationToken) ?? [];
    }

    public async Task<SandboxSession?> FindAsync(string id, CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken)).FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
