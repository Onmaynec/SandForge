using System.Security.Cryptography;
using SandForge.Domain;

namespace SandForge.Core;

public sealed class SessionPlanner(ISecurityPolicyEngine securityPolicyEngine, string? dataDirectory = null) : ISessionPlanner
{
    private readonly string _dataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(Path.GetTempPath(), "SandForge"));

    public async Task<SessionPlan> CreateAsync(TemplateDefinition template, string targetPath, CancellationToken cancellationToken)
    {
        string fullTargetPath = Path.GetFullPath(targetPath);
        if (!File.Exists(fullTargetPath)) throw new FileNotFoundException("Целевой файл не найден.", fullTargetPath);
        string hash = await ComputeSha256Async(fullTargetPath, cancellationToken);
        SecurityEvaluationResult security = await securityPolicyEngine.EvaluateAsync(template, fullTargetPath, cancellationToken);
        if (security.IsBlocked) throw new InvalidOperationException("Политика безопасности заблокировала план сессии.");

        var installers = new List<ProvisioningInstallerPlan>();
        for (int i = 0; i < template.Provisioning.Installers.Count; i++)
        {
            InstallerDefinition installer = template.Provisioning.Installers[i];
            string source = Path.GetFullPath(installer.SourcePath);
            if (!File.Exists(source)) throw new FileNotFoundException("Локальный provisioning installer не найден.", source);
            string actualHash = await ComputeSha256Async(source, cancellationToken);
            if (!string.IsNullOrWhiteSpace(installer.Sha256) && !actualHash.Equals(NormalizeHash(installer.Sha256), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA-256 provisioning installer не совпадает: {Path.GetFileName(source)}.");
            string guestName = $"{i:D2}-{Path.GetFileName(source)}";
            installers.Add(new ProvisioningInstallerPlan
            {
                SourcePath = source,
                GuestPath = $@"C:\Sandbox\Input\provisioning\{guestName}",
                Sha256 = actualHash,
                Arguments = installer.Arguments,
                Timeout = installer.Timeout
            });
        }

        var cacheMounts = new List<SessionMount>();
        if (template.Cache.Enabled)
        {
            var cache = new CacheService(_dataDirectory);
            await cache.EnforceQuotaAsync(template.Cache.Types, template.Cache.MaximumSizeMb, cancellationToken);
            foreach (string type in template.Cache.Types.Distinct(StringComparer.OrdinalIgnoreCase))
                cacheMounts.Add(new SessionMount(cache.GetHostPath(type), CacheService.GetGuestPath(type), MountMode.ReadWrite));
        }

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
            CacheMounts = cacheMounts,
            Target = template.Target,
            ArtifactCollectors = template.ArtifactCollectors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ProvisioningFailurePolicy = template.Provisioning.FailurePolicy,
            Packages = template.Provisioning.Packages,
            Installers = installers,
            Security = security
        };
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string NormalizeHash(string value)
    {
        string normalized = value.Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        if (normalized.Length != 64 || normalized.Any(x => !Uri.IsHexDigit(x))) throw new InvalidDataException("SHA-256 должен содержать 64 hex-символа.");
        return normalized.ToUpperInvariant();
    }
}
