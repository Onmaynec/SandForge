using System.Text.Json;
using SandForge.Domain;

namespace SandForge.Core;

public sealed class CompatibilityService(ITemplateEngine templateEngine)
{
    private static readonly IReadOnlyList<ContractDescriptor> Contracts =
    [
        new()
        {
            Id = "template",
            DisplayName = "SandForge template",
            CurrentVersion = 2,
            SupportedVersions = [1, 2],
            DeprecatedVersions = [1],
            Syntax = ContractSyntax.Yaml,
            SchemaFile = "schemas/template.schema.json",
            FileNames = ["sandforge.yaml", "sandforge.yml"]
        },
        new()
        {
            Id = "config",
            DisplayName = "SandForge configuration",
            CurrentVersion = 2,
            SupportedVersions = [2],
            Syntax = ContractSyntax.Json,
            SchemaFile = "schemas/config.schema.json",
            FileNames = ["sandforge.json"]
        },
        new()
        {
            Id = "report",
            DisplayName = "Session JSON report",
            CurrentVersion = 1,
            SupportedVersions = [0, 1],
            DeprecatedVersions = [0],
            Syntax = ContractSyntax.Json,
            SchemaFile = "schemas/report.schema.json",
            FileNames = ["report.json"]
        },
        new()
        {
            Id = "completion-marker",
            DisplayName = "Guest completion marker",
            CurrentVersion = 1,
            SupportedVersions = [1],
            Syntax = ContractSyntax.Json,
            SchemaFile = "schemas/completion-marker.schema.json",
            FileNames = ["completed.json"]
        },
        new()
        {
            Id = "package-manifest",
            DisplayName = "Portable package manifest",
            CurrentVersion = 1,
            SupportedVersions = [1],
            Syntax = ContractSyntax.Json,
            SchemaFile = "schemas/package-manifest.schema.json",
            FileNames = ["manifest.json"]
        }
    ];

    public IReadOnlyList<ContractDescriptor> ListContracts() => Contracts;

    public ContractDescriptor? FindContract(string id) =>
        Contracts.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public async Task<ContractValidationResult> ValidateAsync(
        string path,
        string? requestedContract,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return Invalid(requestedContract ?? "unknown", fullPath, null, $"File not found: {fullPath}");
        }

        ContractDescriptor? contract = string.IsNullOrWhiteSpace(requestedContract)
            ? await DetectContractAsync(fullPath, cancellationToken)
            : FindContract(requestedContract);
        if (contract is null)
        {
            return Invalid(requestedContract ?? "unknown", fullPath, null,
                string.IsNullOrWhiteSpace(requestedContract)
                    ? "The contract could not be detected. Use --contract <id>."
                    : $"Unknown contract: {requestedContract}.");
        }

        try
        {
            return contract.Syntax == ContractSyntax.Yaml
                ? await ValidateTemplateAsync(fullPath, contract, cancellationToken)
                : await ValidateJsonAsync(fullPath, contract, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            return Invalid(contract.Id, fullPath, null, exception.Message);
        }
    }

    private async Task<ContractDescriptor?> DetectContractAsync(string path, CancellationToken cancellationToken)
    {
        string fileName = Path.GetFileName(path);
        ContractDescriptor? byName = Contracts.FirstOrDefault(x =>
            x.FileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase));
        if (byName is not null && !fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) return byName;

        string extension = Path.GetExtension(path);
        if (extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
            return FindContract("template");
        if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase)) return null;

        await using FileStream stream = File.OpenRead(path);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (Has(root, "session") && Has(root, "language")) return FindContract("report");
        if (Has(root, "sessionId") && Has(root, "targetExitCode")) return FindContract("completion-marker");
        if (Has(root, "product") && Has(root, "runtimeIdentifier") && Has(root, "files")) return FindContract("package-manifest");
        if (Has(root, "storage") && Has(root, "security") && Has(root, "updates")) return FindContract("config");
        return byName;
    }

    private async Task<ContractValidationResult> ValidateTemplateAsync(
        string path,
        ContractDescriptor contract,
        CancellationToken cancellationToken)
    {
        TemplateDefinition template = await templateEngine.LoadAsync(path, cancellationToken);
        var warnings = new List<string>();
        if (contract.DeprecatedVersions.Contains(template.SchemaVersion))
            warnings.Add($"Schema version {template.SchemaVersion} is deprecated; use version {contract.CurrentVersion}.");
        return new ContractValidationResult
        {
            ContractId = contract.Id,
            Path = path,
            DetectedVersion = template.SchemaVersion,
            IsValid = true,
            Warnings = warnings
        };
    }

    private static async Task<ContractValidationResult> ValidateJsonAsync(
        string path,
        ContractDescriptor contract,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        var errors = new List<string>();
        var warnings = new List<string>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("The document root must be a JSON object.");
            return Result(contract.Id, path, null, errors, warnings);
        }

        int? version = ReadVersion(root, contract.Id);
        if (version is null)
        {
            errors.Add("schemaVersion is required.");
        }
        else if (!contract.SupportedVersions.Contains(version.Value))
        {
            errors.Add($"Unsupported {contract.Id} schema version {version}; supported: {string.Join(", ", contract.SupportedVersions)}.");
        }
        else if (contract.DeprecatedVersions.Contains(version.Value))
        {
            warnings.Add($"Schema version {version} is deprecated; use version {contract.CurrentVersion}.");
        }

        switch (contract.Id)
        {
            case "config": ValidateConfig(root, errors); break;
            case "report": ValidateReport(root, version, errors); break;
            case "completion-marker": ValidateCompletionMarker(root, errors); break;
            case "package-manifest": ValidatePackageManifest(root, errors); break;
            default: errors.Add($"No validator is registered for contract {contract.Id}."); break;
        }
        return Result(contract.Id, path, version, errors, warnings);
    }

    private static void ValidateConfig(JsonElement root, List<string> errors)
    {
        foreach (string section in new[] { "storage", "session", "security", "cache", "updates", "privacy", "ui" })
            RequireObject(root, section, errors);
        if (TryGet(root, "ui", out JsonElement ui) && ui.ValueKind == JsonValueKind.Object
            && TryGet(ui, "language", out JsonElement language)
            && language.ValueKind == JsonValueKind.String
            && language.GetString() is string value
            && value is not ("ru" or "en" or "auto"))
            errors.Add("ui.language must be ru, en or auto.");
    }

    private static void ValidateReport(JsonElement root, int? version, List<string> errors)
    {
        RequireString(root, "language", errors);
        RequireObject(root, "session", errors);
        if (version == 1)
        {
            RequireString(root, "generatedAt", errors);
            RequireString(root, "generatorVersion", errors);
        }
        if (TryGet(root, "language", out JsonElement language)
            && language.ValueKind == JsonValueKind.String
            && language.GetString() is string value
            && value is not ("ru" or "en"))
            errors.Add("language must be ru or en.");
    }

    private static void ValidateCompletionMarker(JsonElement root, List<string> errors)
    {
        RequireString(root, "sessionId", errors);
        RequireInteger(root, "targetExitCode", errors);
    }

    private static void ValidatePackageManifest(JsonElement root, List<string> errors)
    {
        RequireString(root, "product", errors);
        RequireString(root, "version", errors);
        RequireString(root, "runtimeIdentifier", errors);
        RequireString(root, "createdAt", errors);
        if (!TryGet(root, "files", out JsonElement files) || files.ValueKind != JsonValueKind.Array)
        {
            errors.Add("files must be an array.");
            return;
        }

        int index = 0;
        foreach (JsonElement file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"files[{index}] must be an object.");
                index++;
                continue;
            }
            RequireString(file, "path", errors, $"files[{index}].");
            RequireInteger(file, "size", errors, $"files[{index}].");
            RequireString(file, "sha256", errors, $"files[{index}].");
            if (TryGet(file, "path", out JsonElement pathElement) && pathElement.ValueKind == JsonValueKind.String)
            {
                string value = pathElement.GetString() ?? string.Empty;
                string[] segments = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (Path.IsPathRooted(value) || segments.Contains("..", StringComparer.Ordinal))
                    errors.Add($"files[{index}].path must be relative and must not contain '..'.");
            }
            if (TryGet(file, "sha256", out JsonElement hashElement) && hashElement.ValueKind == JsonValueKind.String)
            {
                string hash = hashElement.GetString() ?? string.Empty;
                if (hash.Length != 64 || hash.Any(x => !Uri.IsHexDigit(x)))
                    errors.Add($"files[{index}].sha256 must contain 64 hexadecimal characters.");
            }
            index++;
        }
    }

    private static int? ReadVersion(JsonElement root, string contractId)
    {
        if (TryGet(root, "schemaVersion", out JsonElement version)
            && version.ValueKind == JsonValueKind.Number
            && version.TryGetInt32(out int result))
            return result;
        if (contractId == "report" && Has(root, "language") && Has(root, "session")) return 0;
        return null;
    }

    private static void RequireObject(JsonElement root, string name, List<string> errors, string prefix = "")
    {
        if (!TryGet(root, name, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            errors.Add($"{prefix}{name} must be an object.");
    }

    private static void RequireString(JsonElement root, string name, List<string> errors, string prefix = "")
    {
        if (!TryGet(root, name, out JsonElement value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            errors.Add($"{prefix}{name} must be a non-empty string.");
    }

    private static void RequireInteger(JsonElement root, string name, List<string> errors, string prefix = "")
    {
        if (!TryGet(root, name, out JsonElement value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out _))
            errors.Add($"{prefix}{name} must be an integer.");
    }

    private static bool Has(JsonElement element, string name) => TryGet(element, name, out _);

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static ContractValidationResult Invalid(string contractId, string path, int? version, params string[] errors) =>
        Result(contractId, path, version, errors, Array.Empty<string>());

    private static ContractValidationResult Result(
        string contractId,
        string path,
        int? version,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings) => new()
        {
            ContractId = contractId,
            Path = path,
            DetectedVersion = version,
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
}
