using SandForge.Domain;

namespace SandForge.Core;

public sealed class CleanupService(SessionStore store)
{
    public async Task<CleanupResult> CleanupAsync(TimeSpan olderThan, bool orphanedOnly, bool dryRun, CancellationToken cancellationToken)
    {
        if (olderThan < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(olderThan));
        DateTimeOffset threshold = DateTimeOffset.UtcNow - olderThan;
        string sessionsRoot = Path.Combine(store.DataDirectory, "sessions");
        IReadOnlyList<SandboxSession> sessions = await store.LoadAsync(cancellationToken);
        var candidates = new List<CleanupCandidate>();
        foreach (SandboxSession session in sessions)
        {
            bool terminal = session.Status is SessionStatus.Completed or SessionStatus.Partial or SessionStatus.Failed or SessionStatus.Cancelled or SessionStatus.TimedOut or SessionStatus.Orphaned;
            if (!terminal || session.Cleanup is CleanupState.Cleaned or CleanupState.Kept) continue;
            if (orphanedOnly && session.Status != SessionStatus.Orphaned) continue;
            if ((session.EndedAt ?? session.CreatedAt) > threshold) continue;
            if (!IsInside(sessionsRoot, session.WorkspacePath)) continue;
            long size = Directory.Exists(session.WorkspacePath) ? GetSize(session.WorkspacePath) : 0;
            candidates.Add(new CleanupCandidate(session.Id, session.WorkspacePath, size, session.Status));
        }

        if (dryRun) return new CleanupResult(candidates, 0, 0, true);
        int cleaned = 0;
        long reclaimed = 0;
        foreach (CleanupCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SandboxSession? session = await store.FindAsync(candidate.SessionId, cancellationToken);
            if (session is null || !IsInside(sessionsRoot, candidate.WorkspacePath)) continue;
            if (Directory.Exists(candidate.WorkspacePath)) Directory.Delete(candidate.WorkspacePath, recursive: true);
            await store.SaveAsync(session with { Cleanup = CleanupState.Cleaned }, cancellationToken);
            cleaned++;
            reclaimed += candidate.SizeBytes;
        }
        return new CleanupResult(candidates, cleaned, reclaimed, false);
    }

    public static bool IsInside(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(normalizedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static long GetSize(string path)
    {
        try { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length); }
        catch (UnauthorizedAccessException) { return 0; }
        catch (IOException) { return 0; }
    }
}
