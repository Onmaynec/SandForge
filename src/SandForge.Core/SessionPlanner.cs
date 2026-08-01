using System.Security.Cryptography;
using SandForge.Domain;

namespace SandForge.Core;

public sealed class SessionPlanner(ISecurityPolicyEngine securityPolicyEngine) : ISessionPlanner
{
    public async Task<SessionPlan> CreateAsync(TemplateDefinition template, string targetPath, CancellationToken cancellationToken)
    {
        string fullTargetPath = Path.GetFullPath(targetPath);
        if (!File.Exists(fullTargetPath)) throw new FileNotFoundException("Целевой файл не найден.", fullTargetPath);
        string hash = await ComputeSha256Async(fullTargetPath, cancellationToken);
        SecurityEvaluationResult security = await securityPolicyEngine.EvaluateAsync(template, fullTargetPath, cancellationToken);
        if (security.IsBlocked) throw new InvalidOperationException("Политика безопасности заблокировала план сессии.");

        string id = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
        return new SessionPlan
        {
            SessionId = id,
            TemplateName = template.Metadata.Name,
            TargetSourcePath = fullTargetPath,
            TargetFileName = Path.GetFileName(fullTargetPath),
            TargetSha256 = hash,
            Sandbox = template.Sandbox,
            Session = template.Session,
            Mounts = template.Mounts.Select(x => new SessionMount(Environment.ExpandEnvironmentVariables(x.Source), x.Destination, x.Mode)).ToList(),
            Target = template.Target,
            ArtifactCollectors = template.ArtifactCollectors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Security = security
        };
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
