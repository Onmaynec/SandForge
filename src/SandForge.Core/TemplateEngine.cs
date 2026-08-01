using SandForge.Domain;

namespace SandForge.Core;

/// <summary>
/// Safe, intentionally constrained YAML reader for the MVP schema.
/// It accepts only scalar keys and the known mounts/collectors lists.
/// Unknown top-level sections are ignored; arbitrary YAML tags and object construction are impossible.
/// </summary>
public sealed class TemplateEngine : ITemplateEngine
{
    public async Task<TemplateDefinition> LoadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Template file was not found.", fullPath);
        }

        string[] lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        int schemaVersion = 1;
        string section = string.Empty;
        string name = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? "custom";
        string displayName = name;
        string description = string.Empty;
        NetworkPolicy network = NetworkPolicy.Disabled;
        ClipboardPolicy clipboard = ClipboardPolicy.Disabled;
        int memoryMb = 4096;
        bool protectedClient = true;
        TimeSpan timeout = TimeSpan.FromMinutes(15);
        bool keepWorkspace = false;
        string executable = string.Empty;
        string workingDirectory = @"C:\Sandbox\Work";
        bool wait = true;
        var arguments = new List<string>();
        var mounts = new List<MountDefinition>();
        var collectors = new List<string>();
        MountBuilder? currentMount = null;
        bool inTargetArguments = false;
        bool inCollectors = false;

        foreach (string raw in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string noComment = StripComment(raw);
            if (string.IsNullOrWhiteSpace(noComment)) continue;

            int indent = noComment.Length - noComment.TrimStart().Length;
            string line = noComment.Trim();

            if (indent == 0)
            {
                FlushMount();
                inTargetArguments = false;
                inCollectors = false;
                if (TryPair(line, out string rootKey, out string rootValue) && rootKey == "schemaVersion")
                {
                    schemaVersion = ParseInt(rootValue, "schemaVersion");
                    continue;
                }
                section = line.TrimEnd(':');
                continue;
            }

            if (section == "metadata" && TryPair(line, out string metadataKey, out string metadataValue))
            {
                switch (metadataKey)
                {
                    case "name": name = Unquote(metadataValue); break;
                    case "displayName": displayName = Unquote(metadataValue); break;
                    case "description": description = Unquote(metadataValue); break;
                }
            }
            else if (section == "sandbox" && TryPair(line, out string sandboxKey, out string sandboxValue))
            {
                switch (sandboxKey)
                {
                    case "network": network = ParseEnum<NetworkPolicy>(sandboxValue); break;
                    case "clipboard": clipboard = ParseEnum<ClipboardPolicy>(sandboxValue); break;
                    case "memoryMb": memoryMb = ParseInt(sandboxValue, sandboxKey); break;
                    case "protectedClient": protectedClient = ParseBool(sandboxValue, sandboxKey); break;
                }
            }
            else if (section == "session" && TryPair(line, out string sessionKey, out string sessionValue))
            {
                switch (sessionKey)
                {
                    case "timeout": timeout = ParseDuration(sessionValue); break;
                    case "keepWorkspace": keepWorkspace = ParseBool(sessionValue, sessionKey); break;
                }
            }
            else if (section == "mounts")
            {
                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    FlushMount();
                    currentMount = new MountBuilder();
                    string rest = line[2..].Trim();
                    if (TryPair(rest, out string mountKey, out string mountValue)) ApplyMount(mountKey, mountValue);
                }
                else if (currentMount is not null && TryPair(line, out string mountKey, out string mountValue))
                {
                    ApplyMount(mountKey, mountValue);
                }
            }
            else if (section == "target")
            {
                if (line == "arguments:")
                {
                    inTargetArguments = true;
                    continue;
                }
                if (inTargetArguments && line.StartsWith("- ", StringComparison.Ordinal))
                {
                    arguments.Add(Unquote(line[2..].Trim()));
                    continue;
                }
                inTargetArguments = false;
                if (TryPair(line, out string targetKey, out string targetValue))
                {
                    switch (targetKey)
                    {
                        case "executable": executable = Unquote(targetValue); break;
                        case "workingDirectory": workingDirectory = Unquote(targetValue); break;
                        case "wait": wait = ParseBool(targetValue, targetKey); break;
                    }
                }
            }
            else if (section == "artifacts")
            {
                if (line == "collectors:")
                {
                    inCollectors = true;
                    continue;
                }
                if (inCollectors && line.StartsWith("- ", StringComparison.Ordinal))
                {
                    collectors.Add(Unquote(line[2..].Trim()));
                }
            }
        }

        FlushMount();
        if (schemaVersion != 1) throw new InvalidDataException($"Unsupported template schemaVersion: {schemaVersion}.");
        if (memoryMb < 1024) throw new InvalidDataException("sandbox.memoryMb must be at least 1024.");
        if (timeout <= TimeSpan.Zero) throw new InvalidDataException("session.timeout must be positive.");

        return new TemplateDefinition
        {
            SchemaVersion = schemaVersion,
            Metadata = new TemplateMetadata(name, displayName, description),
            Sandbox = new SandboxSettings
            {
                Network = network,
                Clipboard = clipboard,
                MemoryMb = memoryMb,
                ProtectedClient = protectedClient
            },
            Session = new SessionSettings { Timeout = timeout, KeepWorkspace = keepWorkspace },
            Mounts = mounts,
            Target = new TargetDefinition
            {
                Executable = executable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                Wait = wait
            },
            ArtifactCollectors = collectors.Count == 0 ? ["user-output"] : collectors
        };

        void FlushMount()
        {
            if (currentMount is null) return;
            if (string.IsNullOrWhiteSpace(currentMount.Source) || string.IsNullOrWhiteSpace(currentMount.Destination))
                throw new InvalidDataException("Each mount requires source and destination.");
            mounts.Add(new MountDefinition(currentMount.Source, currentMount.Destination, currentMount.Mode));
            currentMount = null;
        }

        void ApplyMount(string key, string value)
        {
            if (currentMount is null) return;
            switch (key)
            {
                case "source": currentMount.Source = Unquote(value); break;
                case "destination": currentMount.Destination = Unquote(value); break;
                case "mode": currentMount.Mode = ParseEnum<MountMode>(value); break;
            }
        }
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
        if (separator < 0)
        {
            key = value = string.Empty;
            return false;
        }
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
        if (!Enum.TryParse(normalized, true, out T result))
            throw new InvalidDataException($"Invalid {typeof(T).Name} value: {value}.");
        return result;
    }

    private static int ParseInt(string value, string key) =>
        int.TryParse(Unquote(value), out int result) ? result : throw new InvalidDataException($"{key} must be an integer.");

    private static bool ParseBool(string value, string key) =>
        bool.TryParse(Unquote(value), out bool result) ? result : throw new InvalidDataException($"{key} must be true or false.");

    private static TimeSpan ParseDuration(string value)
    {
        string text = Unquote(value).Trim();
        if (text.EndsWith('m') && double.TryParse(text[..^1], out double minutes)) return TimeSpan.FromMinutes(minutes);
        if (text.EndsWith('h') && double.TryParse(text[..^1], out double hours)) return TimeSpan.FromHours(hours);
        if (TimeSpan.TryParse(text, out TimeSpan parsed)) return parsed;
        throw new InvalidDataException($"Invalid duration: {value}.");
    }

    private sealed class MountBuilder
    {
        public string Source { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public MountMode Mode { get; set; } = MountMode.ReadOnly;
    }
}
