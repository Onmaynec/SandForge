using SandForge.Domain;

namespace SandForge.Core;

/// <summary>
/// Constrained YAML reader for SandForge templates. The parser supports only the documented
/// scalar fields and lists, never executes YAML tags, and resolves extends/includes inside a
/// trusted template root with cycle and traversal protection.
/// </summary>
public sealed class TemplateEngine : ITemplateEngine
{
    public async Task<TemplateDefinition> LoadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Файл шаблона не найден.", fullPath);

        string root = FindTemplateRoot(fullPath);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<string>();
        TemplatePatch patch = await LoadPatchAsync(fullPath, root, stack, sources, cancellationToken);
        return BuildDefinition(patch, sources);
    }

    private static async Task<TemplatePatch> LoadPatchAsync(
        string path,
        string root,
        HashSet<string> stack,
        List<string> sources,
        CancellationToken cancellationToken)
    {
        string fullPath = EnsureInside(root, path);
        if (!stack.Add(fullPath)) throw new InvalidDataException($"Обнаружен цикл extends/includes: {Path.GetFileName(fullPath)}.");
        try
        {
            ParsedDocument document = await ParseDocumentAsync(fullPath, cancellationToken);
            var result = new TemplatePatch();
            if (!string.IsNullOrWhiteSpace(document.Extends))
            {
                string basePath = ResolveReference(fullPath, root, document.Extends);
                result = Merge(result, await LoadPatchAsync(basePath, root, stack, sources, cancellationToken));
            }
            foreach (string include in document.Includes)
            {
                string includePath = ResolveReference(fullPath, root, include);
                result = Merge(result, await LoadPatchAsync(includePath, root, stack, sources, cancellationToken));
            }
            result = Merge(result, document.Patch);
            if (!sources.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) sources.Add(fullPath);
            return result;
        }
        finally
        {
            stack.Remove(fullPath);
        }
    }

    private static async Task<ParsedDocument> ParseDocumentAsync(string path, CancellationToken cancellationToken)
    {
        string[] lines = await File.ReadAllLinesAsync(path, cancellationToken);
        string directory = Path.GetDirectoryName(path)!;
        var patch = new TemplatePatch();
        var includes = new List<string>();
        string? extends = null;
        string section = string.Empty;
        string subsection = string.Empty;
        bool inTargetArguments = false;
        bool inCollectors = false;
        bool inCacheTypes = false;
        bool inInstallerArguments = false;
        MountBuilder? mount = null;
        PackageBuilder? package = null;
        InstallerBuilder? installer = null;

        foreach (string raw in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string noComment = StripComment(raw);
            if (string.IsNullOrWhiteSpace(noComment)) continue;
            int indent = noComment.Length - noComment.TrimStart().Length;
            string line = noComment.Trim();

            if (indent == 0)
            {
                FlushBuilders();
                subsection = string.Empty;
                inTargetArguments = inCollectors = inCacheTypes = inInstallerArguments = false;
                if (TryPair(line, out string rootKey, out string rootValue))
                {
                    if (rootKey == "schemaVersion") { patch.SchemaVersion = ParseInt(rootValue, rootKey); section = string.Empty; continue; }
                    if (rootKey == "extends") { extends = Unquote(rootValue); section = string.Empty; continue; }
                }
                section = line.TrimEnd(':');
                continue;
            }

            if (section == "includes")
            {
                if (line.StartsWith("- ", StringComparison.Ordinal)) includes.Add(Unquote(line[2..].Trim()));
                continue;
            }

            if (section == "metadata" && TryPair(line, out string metadataKey, out string metadataValue))
            {
                switch (metadataKey)
                {
                    case "name": patch.Name = Unquote(metadataValue); break;
                    case "displayName": patch.DisplayName = Unquote(metadataValue); break;
                    case "description": patch.Description = Unquote(metadataValue); break;
                }
                continue;
            }

            if (section == "sandbox" && TryPair(line, out string sandboxKey, out string sandboxValue))
            {
                switch (sandboxKey)
                {
                    case "network": patch.Network = ParseEnum<NetworkPolicy>(sandboxValue); break;
                    case "clipboard": patch.Clipboard = ParseEnum<ClipboardPolicy>(sandboxValue); break;
                    case "memoryMb": patch.MemoryMb = ParseInt(sandboxValue, sandboxKey); break;
                    case "protectedClient": patch.ProtectedClient = ParseBool(sandboxValue, sandboxKey); break;
                }
                continue;
            }

            if (section == "session" && TryPair(line, out string sessionKey, out string sessionValue))
            {
                switch (sessionKey)
                {
                    case "timeout": patch.Timeout = ParseDuration(sessionValue); break;
                    case "keepWorkspace": patch.KeepWorkspace = ParseBool(sessionValue, sessionKey); break;
                }
                continue;
            }

            if (section == "mounts")
            {
                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    FlushMount();
                    mount = new MountBuilder();
                    string rest = line[2..].Trim();
                    if (TryPair(rest, out string mountKey, out string mountValue)) ApplyMount(mountKey, mountValue);
                }
                else if (mount is not null && TryPair(line, out string mountKey, out string mountValue)) ApplyMount(mountKey, mountValue);
                continue;
            }

            if (section == "target")
            {
                if (line == "arguments:") { inTargetArguments = true; continue; }
                if (inTargetArguments && line.StartsWith("- ", StringComparison.Ordinal)) { patch.TargetArguments.Add(Unquote(line[2..].Trim())); continue; }
                inTargetArguments = false;
                if (TryPair(line, out string targetKey, out string targetValue))
                {
                    switch (targetKey)
                    {
                        case "executable": patch.TargetExecutable = Unquote(targetValue); break;
                        case "workingDirectory": patch.TargetWorkingDirectory = Unquote(targetValue); break;
                        case "wait": patch.TargetWait = ParseBool(targetValue, targetKey); break;
                    }
                }
                continue;
            }

            if (section == "artifacts")
            {
                if (line == "collectors:") { inCollectors = true; continue; }
                if (inCollectors && line.StartsWith("- ", StringComparison.Ordinal)) patch.Collectors.Add(Unquote(line[2..].Trim()));
                continue;
            }

            if (section == "cache")
            {
                if (line == "types:") { inCacheTypes = true; continue; }
                if (inCacheTypes && line.StartsWith("- ", StringComparison.Ordinal)) { patch.CacheTypes.Add(Unquote(line[2..].Trim()).ToLowerInvariant()); continue; }
                inCacheTypes = false;
                if (TryPair(line, out string cacheKey, out string cacheValue))
                {
                    switch (cacheKey)
                    {
                        case "enabled": patch.CacheEnabled = ParseBool(cacheValue, cacheKey); break;
                        case "maximumSizeMb": patch.CacheMaximumSizeMb = ParseInt(cacheValue, cacheKey); break;
                    }
                }
                continue;
            }

            if (section == "provisioning")
            {
                if (indent == 2 && TryPair(line, out string provisioningKey, out string provisioningValue))
                {
                    FlushPackage();
                    FlushInstaller();
                    inInstallerArguments = false;
                    if (provisioningKey == "failurePolicy") patch.ProvisioningFailurePolicy = ParseEnum<ProvisioningFailurePolicy>(provisioningValue);
                    else if (provisioningKey is "packages" or "installers") subsection = provisioningKey;
                    continue;
                }

                if (subsection == "packages")
                {
                    if (line.StartsWith("- ", StringComparison.Ordinal))
                    {
                        FlushPackage();
                        package = new PackageBuilder();
                        string rest = line[2..].Trim();
                        if (TryPair(rest, out string packageKey, out string packageValue)) ApplyPackage(packageKey, packageValue);
                    }
                    else if (package is not null && TryPair(line, out string packageKey, out string packageValue)) ApplyPackage(packageKey, packageValue);
                    continue;
                }

                if (subsection == "installers")
                {
                    if (line.StartsWith("- ", StringComparison.Ordinal) && !inInstallerArguments)
                    {
                        FlushInstaller();
                        installer = new InstallerBuilder();
                        string rest = line[2..].Trim();
                        if (TryPair(rest, out string firstInstallerKey, out string firstInstallerValue)) ApplyInstaller(firstInstallerKey, firstInstallerValue);
                        continue;
                    }
                    if (line == "arguments:") { inInstallerArguments = true; continue; }
                    if (inInstallerArguments && line.StartsWith("- ", StringComparison.Ordinal)) { installer?.Arguments.Add(Unquote(line[2..].Trim())); continue; }
                    inInstallerArguments = false;
                    if (installer is not null && TryPair(line, out string installerKey, out string installerValue)) ApplyInstaller(installerKey, installerValue);
                }
            }
        }

        FlushBuilders();
        return new ParsedDocument(extends, includes, patch);

        void FlushBuilders() { FlushMount(); FlushPackage(); FlushInstaller(); }

        void FlushMount()
        {
            if (mount is null) return;
            if (string.IsNullOrWhiteSpace(mount.Source) || string.IsNullOrWhiteSpace(mount.Destination))
                throw new InvalidDataException("Для каждого mount требуются source и destination.");
            patch.Mounts.Add(new MountDefinition(mount.Source, mount.Destination, mount.Mode));
            mount = null;
        }

        void FlushPackage()
        {
            if (package is null) return;
            if (string.IsNullOrWhiteSpace(package.Id)) throw new InvalidDataException("Для provisioning package требуется id.");
            patch.Packages.Add(new PackageDefinition { Id = package.Id, Version = package.Version, Source = package.Source });
            package = null;
        }

        void FlushInstaller()
        {
            if (installer is null) return;
            if (string.IsNullOrWhiteSpace(installer.Path)) throw new InvalidDataException("Для provisioning installer требуется path.");
            string source = Path.GetFullPath(Path.Combine(directory, Environment.ExpandEnvironmentVariables(installer.Path)));
            patch.Installers.Add(new InstallerDefinition
            {
                SourcePath = source,
                Sha256 = installer.Sha256,
                Arguments = installer.Arguments.ToArray(),
                Timeout = installer.Timeout
            });
            installer = null;
        }

        void ApplyMount(string key, string value)
        {
            if (mount is null) return;
            switch (key)
            {
                case "source": mount.Source = Unquote(value); break;
                case "destination": mount.Destination = Unquote(value); break;
                case "mode": mount.Mode = ParseEnum<MountMode>(value); break;
            }
        }

        void ApplyPackage(string key, string value)
        {
            if (package is null) return;
            switch (key)
            {
                case "id": package.Id = Unquote(value); break;
                case "version": package.Version = EmptyToNull(Unquote(value)); break;
                case "source": package.Source = Unquote(value); break;
            }
        }

        void ApplyInstaller(string key, string value)
        {
            if (installer is null) return;
            switch (key)
            {
                case "path": installer.Path = Unquote(value); break;
                case "sha256": installer.Sha256 = EmptyToNull(Unquote(value)); break;
                case "timeout": installer.Timeout = ParseDuration(value); break;
            }
        }
    }

    private static TemplateDefinition BuildDefinition(TemplatePatch patch, IReadOnlyList<string> sources)
    {
        int schemaVersion = patch.SchemaVersion ?? 1;
        if (schemaVersion is not 1 and not 2) throw new InvalidDataException($"Неподдерживаемая версия схемы шаблона: {schemaVersion}.");
        string name = patch.Name ?? "custom";
        int memory = patch.MemoryMb ?? 4096;
        TimeSpan timeout = patch.Timeout ?? TimeSpan.FromMinutes(15);
        int cacheSize = patch.CacheMaximumSizeMb ?? 2048;
        if (memory < 1024) throw new InvalidDataException("sandbox.memoryMb должен быть не меньше 1024.");
        if (timeout <= TimeSpan.Zero) throw new InvalidDataException("session.timeout должен быть положительным.");
        if (cacheSize is < 64 or > 32768) throw new InvalidDataException("cache.maximumSizeMb должен быть в диапазоне 64–32768.");

        return new TemplateDefinition
        {
            SchemaVersion = schemaVersion,
            Metadata = new TemplateMetadata(name, patch.DisplayName ?? name, patch.Description ?? string.Empty),
            Sandbox = new SandboxSettings
            {
                Network = patch.Network ?? NetworkPolicy.Disabled,
                Clipboard = patch.Clipboard ?? ClipboardPolicy.Disabled,
                MemoryMb = memory,
                ProtectedClient = patch.ProtectedClient ?? true
            },
            Session = new SessionSettings { Timeout = timeout, KeepWorkspace = patch.KeepWorkspace ?? false },
            Mounts = patch.Mounts,
            Target = new TargetDefinition
            {
                Executable = patch.TargetExecutable ?? string.Empty,
                Arguments = patch.TargetArguments,
                WorkingDirectory = patch.TargetWorkingDirectory ?? @"C:\Sandbox\Work",
                Wait = patch.TargetWait ?? true
            },
            ArtifactCollectors = patch.Collectors.Count == 0 ? ["user-output"] : patch.Collectors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Provisioning = new ProvisioningSettings
            {
                FailurePolicy = patch.ProvisioningFailurePolicy ?? ProvisioningFailurePolicy.Stop,
                Packages = patch.Packages,
                Installers = patch.Installers
            },
            Cache = new CacheSettings
            {
                Enabled = patch.CacheEnabled ?? false,
                MaximumSizeMb = cacheSize,
                Types = patch.CacheTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            },
            Sources = sources.ToArray()
        };
    }

    private static TemplatePatch Merge(TemplatePatch left, TemplatePatch right)
    {
        var merged = new TemplatePatch
        {
            SchemaVersion = right.SchemaVersion ?? left.SchemaVersion,
            Name = right.Name ?? left.Name,
            DisplayName = right.DisplayName ?? left.DisplayName,
            Description = right.Description ?? left.Description,
            Network = right.Network ?? left.Network,
            Clipboard = right.Clipboard ?? left.Clipboard,
            MemoryMb = right.MemoryMb ?? left.MemoryMb,
            ProtectedClient = right.ProtectedClient ?? left.ProtectedClient,
            Timeout = right.Timeout ?? left.Timeout,
            KeepWorkspace = right.KeepWorkspace ?? left.KeepWorkspace,
            TargetExecutable = right.TargetExecutable ?? left.TargetExecutable,
            TargetWorkingDirectory = right.TargetWorkingDirectory ?? left.TargetWorkingDirectory,
            TargetWait = right.TargetWait ?? left.TargetWait,
            CacheEnabled = right.CacheEnabled ?? left.CacheEnabled,
            CacheMaximumSizeMb = right.CacheMaximumSizeMb ?? left.CacheMaximumSizeMb,
            ProvisioningFailurePolicy = right.ProvisioningFailurePolicy ?? left.ProvisioningFailurePolicy
        };
        merged.TargetArguments.AddRange(left.TargetArguments);
        if (right.TargetArguments.Count > 0) { merged.TargetArguments.Clear(); merged.TargetArguments.AddRange(right.TargetArguments); }
        merged.Mounts.AddRange(MergeBy(left.Mounts, right.Mounts, x => x.Destination));
        merged.Collectors.AddRange(left.Collectors.Concat(right.Collectors).Distinct(StringComparer.OrdinalIgnoreCase));
        merged.Packages.AddRange(MergeBy(left.Packages, right.Packages, x => x.Id));
        merged.Installers.AddRange(MergeBy(left.Installers, right.Installers, x => x.SourcePath));
        merged.CacheTypes.AddRange(left.CacheTypes.Concat(right.CacheTypes).Distinct(StringComparer.OrdinalIgnoreCase));
        return merged;
    }

    private static IReadOnlyList<T> MergeBy<T>(IEnumerable<T> left, IEnumerable<T> right, Func<T, string> key)
    {
        var order = new List<string>();
        var map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (T item in left.Concat(right))
        {
            string itemKey = key(item);
            if (!map.ContainsKey(itemKey)) order.Add(itemKey);
            map[itemKey] = item;
        }
        return order.Select(x => map[x]).ToArray();
    }

    private static string ResolveReference(string sourceFile, string root, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new InvalidDataException("Пустая ссылка extends/includes.");
        string candidate = Path.IsPathRooted(reference)
            ? reference
            : Path.Combine(Path.GetDirectoryName(sourceFile)!, reference);
        return EnsureInside(root, candidate);
    }

    private static string FindTemplateRoot(string path)
    {
        DirectoryInfo? directory = new(Path.GetDirectoryName(path)!);
        while (directory is not null)
        {
            if (directory.Name.Equals("templates", StringComparison.OrdinalIgnoreCase)) return directory.FullName;
            directory = directory.Parent;
        }
        return Path.GetDirectoryName(path)!;
    }

    private static string EnsureInside(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedCandidate = Path.GetFullPath(candidate);
        if (!normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("extends/includes не может выходить за корень шаблонов.");
        if (!File.Exists(normalizedCandidate)) throw new FileNotFoundException("Подключаемый файл шаблона не найден.", normalizedCandidate);
        return normalizedCandidate;
    }

    private static string StripComment(string line)
    {
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            if (line[i] == '#' && !quoted) return line[..i];
        }
        return line;
    }

    private static bool TryPair(string line, out string key, out string value)
    {
        int separator = line.IndexOf(':');
        if (separator < 0) { key = value = string.Empty; return false; }
        key = line[..separator].Trim();
        value = line[(separator + 1)..].Trim();
        return true;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1].Replace("\\\\", "\\", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal);
        return value;
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum
    {
        string normalized = Unquote(value).Replace("-", string.Empty, StringComparison.Ordinal);
        if (!Enum.TryParse(normalized, true, out T result)) throw new InvalidDataException($"Недопустимое значение {typeof(T).Name}: {value}.");
        return result;
    }

    private static int ParseInt(string value, string key) =>
        int.TryParse(Unquote(value), out int result) ? result : throw new InvalidDataException($"{key} должен быть целым числом.");

    private static bool ParseBool(string value, string key) =>
        bool.TryParse(Unquote(value), out bool result) ? result : throw new InvalidDataException($"{key} должен иметь значение true или false.");

    private static TimeSpan ParseDuration(string value)
    {
        string text = Unquote(value).Trim().ToLowerInvariant();
        if (text.EndsWith('m') && double.TryParse(text[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double minutes)) return TimeSpan.FromMinutes(minutes);
        if (text.EndsWith('h') && double.TryParse(text[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double hours)) return TimeSpan.FromHours(hours);
        if (text.EndsWith('s') && double.TryParse(text[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds)) return TimeSpan.FromSeconds(seconds);
        throw new InvalidDataException($"Недопустимая длительность: {value}.");
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record ParsedDocument(string? Extends, IReadOnlyList<string> Includes, TemplatePatch Patch);

    private sealed class TemplatePatch
    {
        public int? SchemaVersion { get; set; }
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public NetworkPolicy? Network { get; set; }
        public ClipboardPolicy? Clipboard { get; set; }
        public int? MemoryMb { get; set; }
        public bool? ProtectedClient { get; set; }
        public TimeSpan? Timeout { get; set; }
        public bool? KeepWorkspace { get; set; }
        public string? TargetExecutable { get; set; }
        public string? TargetWorkingDirectory { get; set; }
        public bool? TargetWait { get; set; }
        public bool? CacheEnabled { get; set; }
        public int? CacheMaximumSizeMb { get; set; }
        public ProvisioningFailurePolicy? ProvisioningFailurePolicy { get; set; }
        public List<string> TargetArguments { get; } = [];
        public List<MountDefinition> Mounts { get; } = [];
        public List<string> Collectors { get; } = [];
        public List<PackageDefinition> Packages { get; } = [];
        public List<InstallerDefinition> Installers { get; } = [];
        public List<string> CacheTypes { get; } = [];
    }

    private sealed class MountBuilder
    {
        public string Source { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public MountMode Mode { get; set; } = MountMode.ReadOnly;
    }

    private sealed class PackageBuilder
    {
        public string Id { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Source { get; set; } = "winget";
    }

    private sealed class InstallerBuilder
    {
        public string Path { get; set; } = string.Empty;
        public string? Sha256 { get; set; }
        public List<string> Arguments { get; } = [];
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
    }
}
