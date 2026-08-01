using System.Text.Json;
using SandForge.Domain;

namespace SandForge.Core;

public sealed class SessionCoordinator(
    ITemplateEngine templateEngine,
    ISessionPlanner sessionPlanner,
    IWorkspaceManager workspaceManager,
    ISandboxConfigurationGenerator configurationGenerator,
    ISandboxBackend sandboxBackend,
    IArtifactManager artifactManager,
    SessionStore sessionStore,
    string dataDirectory)
{
    public Task<SessionRunResult> RunAsync(string templatePath, string targetPath, CancellationToken cancellationToken) =>
        RunAsync(templatePath, targetPath, progress: null, cancellationToken);

    public async Task<SessionRunResult> RunAsync(
        string templatePath,
        string targetPath,
        IProgress<SessionProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SessionProgress(SessionStatus.Validating));
        TemplateDefinition template = await templateEngine.LoadAsync(templatePath, cancellationToken);
        SessionPlan plan = await sessionPlanner.CreateAsync(template, targetPath, cancellationToken);
        int collectorCount = plan.ArtifactCollectors.Count;

        progress?.Report(new SessionProgress(SessionStatus.Preparing, TotalCollectors: collectorCount));
        SessionWorkspace workspace = await workspaceManager.PrepareAsync(plan, dataDirectory, cancellationToken);
        string configurationPath = await configurationGenerator.GenerateAsync(plan, workspace, cancellationToken);
        var session = new SandboxSession
        {
            Id = plan.SessionId, TemplateId = plan.TemplateName, CreatedAt = DateTimeOffset.UtcNow,
            Status = SessionStatus.Ready, WorkspacePath = workspace.Root, ConfigurationPath = configurationPath,
            TargetFileHash = plan.TargetSha256, Risk = plan.Security.Risk,
            Cleanup = plan.Session.KeepWorkspace ? CleanupState.Kept : CleanupState.Pending
        };
        await sessionStore.SaveAsync(session, cancellationToken);
        progress?.Report(new SessionProgress(SessionStatus.Ready, TotalCollectors: collectorCount));

        SandboxAvailabilityResult availability = await sandboxBackend.CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsAvailable)
            return await FailAsync(session, availability.Message, collectorCount, progress, cancellationToken);

        session = session with { Status = SessionStatus.Starting, StartedAt = DateTimeOffset.UtcNow };
        await sessionStore.SaveAsync(session, cancellationToken);
        progress?.Report(new SessionProgress(SessionStatus.Starting, TotalCollectors: collectorCount));
        SandboxLaunchResult launch = await sandboxBackend.LaunchAsync(configurationPath, cancellationToken);
        if (!launch.Started)
            return await FailAsync(session, launch.Message, collectorCount, progress, cancellationToken);

        session = session with { Status = SessionStatus.Running, SandboxProcessId = launch.ProcessId };
        await sessionStore.SaveAsync(session, cancellationToken);
        progress?.Report(new SessionProgress(SessionStatus.Running, TotalCollectors: collectorCount));
        string marker = Path.Combine(workspace.Output, ".sandforge", "completed.json");
        if (!await WaitForCompletionAsync(marker, plan.Session.Timeout, cancellationToken))
        {
            ArtifactImportResult partialImport;
            try { partialImport = await artifactManager.ImportAsync(workspace, cancellationToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                partialImport = new ArtifactImportResult(Array.Empty<SessionArtifact>(), Array.Empty<CollectorResult>());
            }
            session = session with
            {
                Status = SessionStatus.TimedOut,
                EndedAt = DateTimeOffset.UtcNow,
                Artifacts = partialImport.Artifacts,
                Collectors = partialImport.Collectors,
                Error = "Маркер завершения не получен до истечения timeout."
            };
            await sessionStore.SaveAsync(session, cancellationToken);
            progress?.Report(new SessionProgress(
                session.Status,
                partialImport.Collectors.Count,
                collectorCount));
            return new SessionRunResult(session, null);
        }

        CompletionMarker completion = await ReadMarkerAsync(marker, plan.SessionId, cancellationToken);
        session = session with { Status = SessionStatus.Collecting };
        await sessionStore.SaveAsync(session, cancellationToken);
        progress?.Report(new SessionProgress(SessionStatus.Collecting, TotalCollectors: collectorCount));
        ArtifactImportResult imported = await artifactManager.ImportAsync(workspace, cancellationToken);
        bool collectorFailed = imported.Collectors.Any(x => !string.IsNullOrWhiteSpace(x.Error));
        session = session with
        {
            Status = completion.TargetExitCode == 0 && !collectorFailed ? SessionStatus.Completed : SessionStatus.Partial,
            EndedAt = DateTimeOffset.UtcNow,
            Artifacts = imported.Artifacts,
            Collectors = imported.Collectors,
            Error = completion.TargetExitCode != 0
                ? $"Целевой процесс завершился с кодом {completion.TargetExitCode}."
                : collectorFailed ? "Один или несколько collectors завершились с ошибкой." : null
        };
        await sessionStore.SaveAsync(session, cancellationToken);
        progress?.Report(new SessionProgress(session.Status, imported.Collectors.Count, collectorCount));
        return new SessionRunResult(session, null);
    }

    private async Task<SessionRunResult> FailAsync(
        SandboxSession session,
        string error,
        int collectorCount,
        IProgress<SessionProgress>? progress,
        CancellationToken cancellationToken)
    {
        session = session with { Status = SessionStatus.Failed, EndedAt = DateTimeOffset.UtcNow, Error = error };
        await sessionStore.SaveAsync(session, cancellationToken);
        progress?.Report(new SessionProgress(SessionStatus.Failed, TotalCollectors: collectorCount));
        return new SessionRunResult(session, null);
    }

    internal static async Task<CompletionMarker> ReadMarkerAsync(string path, string expectedSessionId, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        CompletionMarker marker = await JsonSerializer.DeserializeAsync<CompletionMarker>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
            ?? throw new InvalidDataException("Маркер завершения пуст.");
        if (marker.SchemaVersion != 1 || !marker.SessionId.Equals(expectedSessionId, StringComparison.Ordinal))
            throw new InvalidDataException("Маркер завершения не прошёл проверку.");
        return marker;
    }

    private static async Task<bool> WaitForCompletionAsync(string marker, TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(marker)) return true;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        return false;
    }

    internal sealed record CompletionMarker(int SchemaVersion, string SessionId, int TargetExitCode);
}
