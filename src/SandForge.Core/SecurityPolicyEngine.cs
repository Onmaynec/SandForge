using SandForge.Domain;

namespace SandForge.Core;

public sealed class SecurityPolicyEngine : ISecurityPolicyEngine
{
    private static readonly string[] SensitiveSegments =
    [
        @"\windows", @"\program files", @"\programdata", @"\.ssh", @"\.gnupg",
        @"\appdata\roaming\microsoft\credentials", @"\users"
    ];

    public Task<SecurityEvaluationResult> EvaluateAsync(
        TemplateDefinition template,
        string targetPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var findings = new List<SecurityFinding>();

        if (template.Sandbox.Network != NetworkPolicy.Disabled)
            findings.Add(new(RiskLevel.Medium, "NETWORK_ENABLED", "Сетевой доступ Sandbox включён.", false));
        if (template.Sandbox.Clipboard == ClipboardPolicy.Enabled)
            findings.Add(new(RiskLevel.Medium, "CLIPBOARD_ENABLED", "Перенаправление буфера обмена включено.", false));
        if (template.Session.Timeout > TimeSpan.FromHours(2))
            findings.Add(new(RiskLevel.High, "LONG_TIMEOUT", "Timeout сессии превышает два часа.", false));

        if (template.Provisioning.Packages.Count > 0 && template.Sandbox.Network == NetworkPolicy.Disabled)
            findings.Add(new(RiskLevel.Critical, "PROVISIONING_NETWORK_DISABLED", "Package provisioning требует включённой сети Sandbox.", true));
        foreach (PackageDefinition package in template.Provisioning.Packages)
        {
            if (!package.Source.Equals("winget", StringComparison.OrdinalIgnoreCase))
                findings.Add(new(RiskLevel.Critical, "UNSUPPORTED_PACKAGE_SOURCE", $"Источник package provisioning не поддерживается: {package.Source}.", true));
            if (string.IsNullOrWhiteSpace(package.Version))
                findings.Add(new(RiskLevel.Medium, "UNPINNED_PACKAGE", $"Пакет {package.Id} не закреплён на версии.", false));
        }
        if (template.Provisioning.Installers.Any(x => string.IsNullOrWhiteSpace(x.Sha256)))
            findings.Add(new(RiskLevel.Medium, "INSTALLER_HASH_AUTO", "SHA-256 локального installer будет вычислен автоматически перед запуском.", false));

        if (template.Cache.Enabled)
        {
            findings.Add(new(RiskLevel.Medium, "MANAGED_CACHE_WRITABLE", "Включён отдельный управляемый cache с записью из guest.", false));
            foreach (string type in template.Cache.Types)
            {
                if (!CacheService.AllowedTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                    findings.Add(new(RiskLevel.Critical, "UNKNOWN_CACHE_TYPE", $"Неизвестный тип cache: {type}.", true));
            }
        }

        foreach (MountDefinition mount in template.Mounts)
        {
            string expanded = Environment.ExpandEnvironmentVariables(mount.Source);
            string fullPath = Path.GetFullPath(expanded);
            string normalized = fullPath.TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
            bool root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar)
                .Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) == true;
            bool sensitive = root || SensitiveSegments.Any(segment => normalized.Contains(segment, StringComparison.OrdinalIgnoreCase));

            if (mount.Mode == MountMode.ReadWrite)
                findings.Add(new(RiskLevel.High, "WRITABLE_HOST_MOUNT", $"Host-папка подключена с правом записи: {Redact(fullPath)}", false));
            if (sensitive && mount.Mode == MountMode.ReadWrite)
                findings.Add(new(RiskLevel.Critical, "SENSITIVE_WRITABLE_MOUNT", $"Заблокирован чувствительный путь с правом записи: {Redact(fullPath)}", true));
        }

        string extension = Path.GetExtension(targetPath);
        if (string.IsNullOrWhiteSpace(extension))
            findings.Add(new(RiskLevel.Medium, "UNKNOWN_TARGET_TYPE", "У целевого файла отсутствует расширение.", false));

        RiskLevel risk = findings.Count == 0 ? RiskLevel.Low : findings.Max(x => x.Level);
        return Task.FromResult(new SecurityEvaluationResult { Risk = risk, Findings = findings });
    }

    private static string Redact(string path)
    {
        string? root = Path.GetPathRoot(path);
        return string.IsNullOrEmpty(root) ? "<host-path>" : $"{root}<redacted>";
    }
}
