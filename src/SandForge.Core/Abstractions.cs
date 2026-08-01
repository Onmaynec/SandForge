using SandForge.Domain;

namespace SandForge.Core;

public interface ITemplateEngine
{
    Task<TemplateDefinition> LoadAsync(string path, CancellationToken cancellationToken);
}

public interface ISecurityPolicyEngine
{
    Task<SecurityEvaluationResult> EvaluateAsync(
        TemplateDefinition template,
        string targetPath,
        CancellationToken cancellationToken);
}

public interface ISessionPlanner
{
    Task<SessionPlan> CreateAsync(
        TemplateDefinition template,
        string targetPath,
        CancellationToken cancellationToken);
}

public interface IWorkspaceManager
{
    Task<SessionWorkspace> PrepareAsync(SessionPlan plan, string dataDirectory, CancellationToken cancellationToken);
}

public interface ISandboxConfigurationGenerator
{
    Task<string> GenerateAsync(SessionPlan plan, SessionWorkspace workspace, CancellationToken cancellationToken);
}

public interface ISandboxBackend
{
    Task<SandboxAvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken);
    Task<SandboxLaunchResult> LaunchAsync(string configurationPath, CancellationToken cancellationToken);
}

public interface IArtifactManager
{
    Task<IReadOnlyList<SessionArtifact>> ImportAsync(SessionWorkspace workspace, CancellationToken cancellationToken);
}
