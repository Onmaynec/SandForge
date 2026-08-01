namespace SandForge.Domain;

public enum NetworkPolicy { Disabled, Enabled, Required }
public enum ClipboardPolicy { Disabled, Enabled }
public enum MountMode { ReadOnly, ReadWrite, CopyIn, CopyOut }
public enum RiskLevel { Low, Medium, High, Critical }
public enum SessionStatus
{
    Created, Validating, Preparing, Ready, Starting, Running, Stopping,
    Collecting, Completed, Partial, Failed, Cancelled, TimedOut, Orphaned
}

public sealed record TemplateMetadata(string Name, string DisplayName, string Description);

public sealed record SandboxSettings
{
    public NetworkPolicy Network { get; init; } = NetworkPolicy.Disabled;
    public ClipboardPolicy Clipboard { get; init; } = ClipboardPolicy.Disabled;
    public int MemoryMb { get; init; } = 4096;
    public bool ProtectedClient { get; init; } = true;
}

public sealed record SessionSettings
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);
    public bool KeepWorkspace { get; init; }
}

public sealed record MountDefinition(string Source, string Destination, MountMode Mode);

public sealed record TargetDefinition
{
    public string Executable { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string WorkingDirectory { get; init; } = @"C:\Sandbox\Work";
    public bool Wait { get; init; } = true;
}

public sealed record TemplateDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required TemplateMetadata Metadata { get; init; }
    public SandboxSettings Sandbox { get; init; } = new();
    public SessionSettings Session { get; init; } = new();
    public IReadOnlyList<MountDefinition> Mounts { get; init; } = Array.Empty<MountDefinition>();
    public TargetDefinition Target { get; init; } = new();
    public IReadOnlyList<string> ArtifactCollectors { get; init; } = ["user-output"];
}

public sealed record SecurityFinding(RiskLevel Level, string Code, string Message, bool BlocksLaunch);

public sealed record SecurityEvaluationResult
{
    public required RiskLevel Risk { get; init; }
    public required IReadOnlyList<SecurityFinding> Findings { get; init; }
    public bool IsBlocked => Findings.Any(x => x.BlocksLaunch);
}

public sealed record SessionMount(string HostPath, string GuestPath, MountMode Mode);

public sealed record SessionPlan
{
    public required string SessionId { get; init; }
    public required string TemplateName { get; init; }
    public required string TargetSourcePath { get; init; }
    public required string TargetFileName { get; init; }
    public required string TargetSha256 { get; init; }
    public required SandboxSettings Sandbox { get; init; }
    public required SessionSettings Session { get; init; }
    public required IReadOnlyList<SessionMount> Mounts { get; init; }
    public required TargetDefinition Target { get; init; }
    public required SecurityEvaluationResult Security { get; init; }
}

public sealed record SessionWorkspace
{
    public required string Root { get; init; }
    public required string Input { get; init; }
    public required string Output { get; init; }
    public required string Bootstrap { get; init; }
    public required string Config { get; init; }
    public required string Artifacts { get; init; }
    public required string Logs { get; init; }
    public required string Metadata { get; init; }
}

public sealed record SessionArtifact
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string RelativePath { get; init; }
    public required long Size { get; init; }
    public required string Sha256 { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record SandboxSession
{
    public required string Id { get; init; }
    public required string TemplateId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public required SessionStatus Status { get; init; }
    public required string WorkspacePath { get; init; }
    public required string ConfigurationPath { get; init; }
    public required string TargetFileHash { get; init; }
    public required RiskLevel Risk { get; init; }
    public IReadOnlyList<SessionArtifact> Artifacts { get; init; } = Array.Empty<SessionArtifact>();
    public string? Error { get; init; }
}

public sealed record SandboxAvailabilityResult(bool IsAvailable, string Message);
public sealed record SandboxLaunchResult(bool Started, int? ProcessId, string Message);
public sealed record SessionRunResult(SandboxSession Session, string? ReportPath);
