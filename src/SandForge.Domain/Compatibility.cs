namespace SandForge.Domain;

public enum ContractSyntax
{
    Json,
    Yaml
}

public sealed record ContractDescriptor
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required int CurrentVersion { get; init; }
    public required IReadOnlyList<int> SupportedVersions { get; init; }
    public IReadOnlyList<int> DeprecatedVersions { get; init; } = Array.Empty<int>();
    public required ContractSyntax Syntax { get; init; }
    public required string SchemaFile { get; init; }
    public IReadOnlyList<string> FileNames { get; init; } = Array.Empty<string>();
}

public sealed record ContractValidationResult
{
    public required string ContractId { get; init; }
    public required string Path { get; init; }
    public int? DetectedVersion { get; init; }
    public required bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record SessionReportDocument
{
    public int SchemaVersion { get; init; } = 1;
    public required string Language { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string GeneratorVersion { get; init; }
    public required SandboxSession Session { get; init; }
}

public sealed record PackageManifestDocument
{
    public int SchemaVersion { get; init; } = 1;
    public required string Product { get; init; }
    public required string Version { get; init; }
    public required string RuntimeIdentifier { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required IReadOnlyList<PackageManifestFile> Files { get; init; }
}

public sealed record PackageManifestFile
{
    public required string Path { get; init; }
    public required long Size { get; init; }
    public required string Sha256 { get; init; }
}
