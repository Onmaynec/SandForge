using System.Diagnostics;
using System.Text.Json;
using SandForge.Domain;

namespace SandForge.Core;

public sealed class SessionRecoveryService(SessionStore store, IArtifactManager artifactManager)
{
    public async Task<RecoveryResult> RecoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SandboxSession> all = await store.LoadAsync(cancellationToken);
        SandboxSession[] candidates = all.Where(x => x.Status is SessionStatus.Starting or SessionStatus.Running or SessionStatus.Collecting).ToArray();
        int recovered = 0, orphaned = 0, failed = 0;
        foreach (SandboxSession session in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SessionWorkspace workspace = SessionWorkspace.FromRoot(session.WorkspacePath);
                string markerPath = Path.Combine(workspace.Output, ".sandforge", "completed.json");
                if (!File.Exists(markerPath))
                {
                    if (IsProcessAlive(session.SandboxProcessId)) continue;
                    await store.SaveAsync(session with
                    {
                        Status = SessionStatus.Orphaned,
                        EndedAt = DateTimeOffset.UtcNow,
                        Error = "Процесс Windows Sandbox не найден, маркер завершения отсутствует."
                    }, cancellationToken);
                    orphaned++;
                    continue;
                }

                SessionCoordinator.CompletionMarker marker = await SessionCoordinator.ReadMarkerAsync(markerPath, session.Id, cancellationToken);
                ArtifactImportResult imported = await artifactManager.ImportAsync(workspace, cancellationToken);
                await store.SaveAsync(session with
                {
                    Status = marker.TargetExitCode == 0 ? SessionStatus.Completed : SessionStatus.Partial,
                    EndedAt = DateTimeOffset.UtcNow,
                    Artifacts = imported.Artifacts,
                    Collectors = imported.Collectors,
                    Error = marker.TargetExitCode == 0 ? null : $"Восстановлено после аварии; код выхода {marker.TargetExitCode}."
                }, cancellationToken);
                recovered++;
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            {
                await store.SaveAsync(session with
                {
                    Status = SessionStatus.Orphaned,
                    EndedAt = DateTimeOffset.UtcNow,
                    Error = $"Ошибка восстановления: {exception.Message}"
                }, cancellationToken);
                failed++;
            }
        }
        return new RecoveryResult(candidates.Length, recovered, orphaned, failed);
    }

    private static bool IsProcessAlive(int? processId)
    {
        if (processId is null) return false;
        try
        {
            using Process process = Process.GetProcessById(processId.Value);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
