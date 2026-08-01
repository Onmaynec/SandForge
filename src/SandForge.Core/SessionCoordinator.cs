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
    public async Task<SessionRunResult> RunAsync(
        string templatePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        TemplateDefinition template = await templateEngine.LoadAsync(templatePath, cancellationToken);
        SessionPlan plan = await sessionPlanner.CreateAsync(template, targetPath, cancellationToken);
        SessionWorkspace workspace = await workspaceManager.PrepareAsync(plan, dataDirectory, cancellationToken);
        string configurationPath = await configurationGenerator.GenerateAsync(plan, workspace, cancellationToken);

        var session = new SandboxSession
        {
            Id = plan.SessionId,
            TemplateId = plan.TemplateName,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = SessionStatus.Ready,
            WorkspacePath = workspace.Root,
            ConfigurationPath = configurationPath,
            TargetFileHash = plan.TargetSha256,
            Risk = plan.Security.Risk
        };
        await sessionStore.SaveAsync(session, cancellationToken);

        SandboxAvailabilityResult availability = await sandboxBackend.CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            session = session with { Status = SessionStatus.Failed, EndedAt = DateTimeOffset.UtcNow, Error = availability.Message };
            await sessionStore.SaveAsync(session, cancellationToken);
            return new SessionRunResult(session, null);
        }

        session = session with { Status = SessionStatus.Starting, StartedAt = DateTimeOffset.UtcNow };
        await sessionStore.SaveAsync(session, cancellationToken);
        SandboxLaunchResult launch = await sandboxBackend.LaunchAsync(configurationPath, cancellationToken);
        if (!launch.Started)
        {
            session = session with { Status = SessionStatus.Failed, EndedAt = DateTimeOffset.UtcNow, Error = launch.Message };
            await sessionStore.SaveAsync(session, cancellationToken);
            return new SessionRunResult(session, null);
        }

        session = session with { Status = SessionStatus.Running };
        await sessionStore.SaveAsync(session, cancellationToken);

        string marker = Path.Combine(workspace.Output, ".sandforge", "completed.json");
        bool completed = await WaitForCompletionAsync(marker, plan.Session.Timeout, cancellationToken);
        if (!completed)
        {
            session = session with { Status = SessionStatus.TimedOut, EndedAt = DateTimeOffset.UtcNow, Error = "Completion marker was not received before timeout." };
            await sessionStore.SaveAsync(session, cancellationToken);
            return new SessionRunResult(session, null);
        }

        CompletionMarker completion = await ReadMarkerAsync(marker, plan.SessionId, cancellationToken);
        IReadOnlyList<SessionArtifact> artifacts = await artifactManager.ImportAsync(workspace, cancellationToken);
        session = session with
        {
            Status = completion.TargetExitCode == 0 ? SessionStatus.Completed : SessionStatus.Partial,
            EndedAt = DateTimeOffset.UtcNow,
            Artifacts = artifacts,
            Error = completion.TargetExitCode == 0 ? null : $"Target exited with code {completion.TargetExitCode}."
        };
        await sessionStore.SaveAsync(session, cancellationToken);
        return new SessionRunResult(session, null);
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

    private static async Task<CompletionMarker> ReadMarkerAsync(string path, string expectedSessionId, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        CompletionMarker marker = await JsonSerializer.DeserializeAsync<CompletionMarker>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
            ?? throw new InvalidDataException("Completion marker is empty.");
        if (marker.SchemaVersion != 1 || !marker.SessionId.Equals(expectedSessionId, StringComparison.Ordinal))
            throw new InvalidDataException("Completion marker validation failed.");
        return marker;
    }

    private sealed record CompletionMarker(int SchemaVersion, string SessionId, int TargetExitCode);
}
