namespace SandForge.Domain;

public enum NetworkPolicy { Disabled, Enabled, Required }
public enum ClipboardPolicy { Disabled, Enabled }
public enum MountMode { ReadOnly, ReadWrite, CopyIn, CopyOut }
public enum RiskLevel { Low, Medium, High, Critical }
public enum CleanupState { Pending, Kept, Cleaned }
public enum ProvisioningFailurePolicy { Stop, Continue }
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

public sealed record PackageDefinition
{
    public required string Id { get; init; }
    public string? Version { get; init; }
    public string Source { get; init; } = "winget";
}

public sealed record InstallerDefinition
{
    public required string SourcePath { get; init; }
    public string? Sha256 { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
}

public sealed record ProvisioningSettings
{
    public ProvisioningFailurePolicy FailurePolicy { get; init; } = ProvisioningFailurePolicy.Stop;
    public IReadOnlyList<PackageDefinition> Packages { get; init; } = Array.Empty<PackageDefinition>();
    public IReadOnlyList<InstallerDefinition> Installers { get; init; } = Array.Empty<InstallerDefinition>();
}

public sealed record CacheSettings
{
    public bool Enabled { get; init; }
    public int MaximumSizeMb { get; init; } = 2048;
    public IReadOnlyList<string> Types { get; init; } = Array.Empty<string>();
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
    public ProvisioningSettings Provisioning { get; init; } = new();
    public CacheSettings Cache { get; init; } = new();
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();
}

public sealed record SecurityFinding(RiskLevel Level, string Code, string Message, bool BlocksLaunch);

public sealed record SecurityEvaluationResult
{
    public required RiskLevel Risk { get; init; }
    public required IReadOnlyList<SecurityFinding> Findings { get; init; }
    public bool IsBlocked => Findings.Any(x => x.BlocksLaunch);
}

public sealed record SessionMount(string HostPath, string GuestPath, MountMode Mode);

public sealed record ProvisioningInstallerPlan
{
    public required string SourcePath { get; init; }
    public required string GuestPath { get; init; }
    public required string Sha256 { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required TimeSpan Timeout { get; init; }
}

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
    public required IReadOnlyList<SessionMount> CacheMounts { get; init; }
    public required TargetDefinition Target { get; init; }
    public required IReadOnlyList<string> ArtifactCollectors { get; init; }
    public required ProvisioningFailurePolicy ProvisioningFailurePolicy { get; init; }
    public required IReadOnlyList<PackageDefinition> Packages { get; init; }
    public required IReadOnlyList<ProvisioningInstallerPlan> Installers { get; init; }
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

    public static SessionWorkspace FromRoot(string root) => new()
    {
        Root = root,
        Input = Path.Combine(root, "input"),
        Output = Path.Combine(root, "output"),
        Bootstrap = Path.Combine(root, "bootstrap"),
        Config = Path.Combine(root, "config"),
        Artifacts = Path.Combine(root, "artifacts"),
        Logs = Path.Combine(root, "logs"),
        Metadata = Path.Combine(root, "metadata")
    };
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

public sealed record CollectorResult
{
    public required string Id { get; init; }
    public required string RelativePath { get; init; }
    public int ItemCount { get; init; }
    public string? Error { get; init; }
}

public sealed record ArtifactImportResult(
    IReadOnlyList<SessionArtifact> Artifacts,
    IReadOnlyList<CollectorResult> Collectors);

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
    public int? SandboxProcessId { get; init; }
    public IReadOnlyList<SessionArtifact> Artifacts { get; init; } = Array.Empty<SessionArtifact>();
    public IReadOnlyList<CollectorResult> Collectors { get; init; } = Array.Empty<CollectorResult>();
    public CleanupState Cleanup { get; init; } = CleanupState.Pending;
    public string? Error { get; init; }
}

public sealed record CleanupCandidate(string SessionId, string WorkspacePath, long SizeBytes, SessionStatus Status);
public sealed record CleanupResult(IReadOnlyList<CleanupCandidate> Candidates, int CleanedCount, long ReclaimedBytes, bool DryRun);
public sealed record RecoveryResult(int Inspected, int Recovered, int Orphaned, int Failed);
public sealed record SandboxAvailabilityResult(bool IsAvailable, string Message);
public sealed record SandboxLaunchResult(bool Started, int? ProcessId, string Message);
public sealed record SessionRunResult(SandboxSession Session, string? ReportPath);

public sealed record CacheEntry(string Type, string Path, long SizeBytes, DateTimeOffset LastWriteTime);
public sealed record CacheCleanupResult(int RemovedEntries, long ReclaimedBytes, bool DryRun);
public sealed record UpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    bool IsUpdateAvailable,
    string? PackageUrl,
    string? ChecksumUrl,
    string Message);
public sealed record UpdateApplyResult(bool Started, string Message, string? ScriptPath);
