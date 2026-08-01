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
            findings.Add(new(RiskLevel.Medium, "NETWORK_ENABLED", "Sandbox network access is enabled.", false));
        if (template.Sandbox.Clipboard == ClipboardPolicy.Enabled)
            findings.Add(new(RiskLevel.Medium, "CLIPBOARD_ENABLED", "Clipboard redirection is enabled.", false));
        if (template.Session.Timeout > TimeSpan.FromHours(2))
            findings.Add(new(RiskLevel.High, "LONG_TIMEOUT", "Session timeout exceeds two hours.", false));

        foreach (MountDefinition mount in template.Mounts)
        {
            string expanded = Environment.ExpandEnvironmentVariables(mount.Source);
            string fullPath = Path.GetFullPath(expanded);
            string normalized = fullPath.TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
            bool root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar)
                .Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) == true;
            bool sensitive = root || SensitiveSegments.Any(segment => normalized.Contains(segment, StringComparison.OrdinalIgnoreCase));

            if (mount.Mode == MountMode.ReadWrite)
                findings.Add(new(RiskLevel.High, "WRITABLE_HOST_MOUNT", $"Writable host mount: {Redact(fullPath)}", false));
            if (sensitive && mount.Mode == MountMode.ReadWrite)
                findings.Add(new(RiskLevel.Critical, "SENSITIVE_WRITABLE_MOUNT", $"Blocked writable sensitive path: {Redact(fullPath)}", true));
        }

        string extension = Path.GetExtension(targetPath);
        if (string.IsNullOrWhiteSpace(extension))
            findings.Add(new(RiskLevel.Medium, "UNKNOWN_TARGET_TYPE", "Target has no file extension.", false));

        RiskLevel risk = findings.Count == 0 ? RiskLevel.Low : findings.Max(x => x.Level);
        return Task.FromResult(new SecurityEvaluationResult { Risk = risk, Findings = findings });
    }

    private static string Redact(string path)
    {
        string? root = Path.GetPathRoot(path);
        return string.IsNullOrEmpty(root) ? "<host-path>" : $"{root}<redacted>";
    }
}
