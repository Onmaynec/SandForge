using System.Globalization;
using System.Text.Json;
using SandForge.Cli;
using SandForge.Core;
using SandForge.Domain;
using SandForge.Reporting;
using SandForge.Sandbox;

return await SandForgeProgram.RunAsync(args);

internal static class SandForgeProgram
{
    private const string CurrentVersion = "0.4.0-alpha";
    private static UiText Text { get; set; } = UiText.Russian;

    public static async Task<int> RunAsync(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        string baseDirectory = AppContext.BaseDirectory;
        string dataDirectory = File.Exists(Path.Combine(baseDirectory, "portable.mode"))
            ? Path.Combine(baseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SandForge");
        Directory.CreateDirectory(dataDirectory);

        UiConfiguration uiConfiguration = UiConfiguration.Load(baseDirectory);
        Text = UiText.FromSetting(uiConfiguration.Language);

        var templateEngine = new TemplateEngine();
        var security = new SecurityPolicyEngine();
        var planner = new SessionPlanner(security, dataDirectory);
        var workspace = new WorkspaceManager();
        var generator = new SandboxConfigurationGenerator();
        var backend = new WindowsSandboxBackend();
        var artifacts = new ArtifactManager();
        var store = new SessionStore(dataDirectory);
        var reports = new ReportWriter(Text);
        var coordinator = new SessionCoordinator(templateEngine, planner, workspace, generator, backend, artifacts, store, dataDirectory);
        var recovery = new SessionRecoveryService(store, artifacts);
        var cleanup = new CleanupService(store);
        var cache = new CacheService(dataDirectory);
        var updateSettingsStore = new UpdateSettingsStore(dataDirectory);
        var updater = new UpdateService(baseDirectory, dataDirectory);

        try
        {
            int? automaticUpdateExit = await TryAutomaticUpdateAsync(args, updater, updateSettingsStore, cts.Token);
            if (automaticUpdateExit.HasValue) return automaticUpdateExit.Value;
            if (args.Length == 0)
            {
                if (Console.IsInputRedirected || Console.IsOutputRedirected) return Help();
                var tui = new TuiApplication(
                    templateEngine, planner, coordinator, backend, store, reports, recovery, cleanup, cache,
                    updater, updateSettingsStore, dataDirectory, baseDirectory, Text, uiConfiguration.Animations);
                return await tui.RunAsync(cts.Token);
            }

            return args[0].ToLowerInvariant() switch
            {
                "--help" or "-h" or "help" => Help(),
                "--version" or "version" => Version(),
                "doctor" or "status" => await DoctorAsync(backend, store, cache, updateSettingsStore, dataDirectory, cts.Token),
                "run" => await RunTargetAsync(args, coordinator, reports, baseDirectory, "isolated-analysis", cts.Token),
                "run-script" => await RunTargetAsync(args, coordinator, reports, baseDirectory, "powershell-clean", cts.Token),
                "test-installer" => await RunTargetAsync(args, coordinator, reports, baseDirectory, "installer-test", cts.Token),
                "matrix" => await MatrixAsync(args, coordinator, reports, baseDirectory, cts.Token),
                "session" => await SessionAsync(args, store, reports, dataDirectory, cts.Token),
                "report" => await ReportAsync(args, store, reports, dataDirectory, cts.Token),
                "recover" => await RecoverAsync(recovery, cts.Token),
                "cleanup" => await CleanupAsync(args, cleanup, cts.Token),
                "cache" => await CacheAsync(args, cache, cts.Token),
                "update" => await UpdateAsync(args, updater, updateSettingsStore, cts.Token),
                _ => Unknown(args[0])
            };
        }
        catch (OperationCanceledException) { Console.Error.WriteLine(Text["Error_OperationCancelled"]); return 12; }
        catch (FileNotFoundException e) { Console.Error.WriteLine(e.Message); return 3; }
        catch (InvalidDataException e) { Console.Error.WriteLine(e.Message); return 4; }
        catch (InvalidOperationException e) when (e.Message.Contains("безопасност", StringComparison.OrdinalIgnoreCase)) { Console.Error.WriteLine(e.Message); return 7; }
        catch (Exception e) { Console.Error.WriteLine(Text.Format("Error_Generic", e.Message)); return 1; }
    }

    private static int Help()
    {
        Console.WriteLine(Text["Help_Text"]);
        return 0;
    }

    private static int Version() { Console.WriteLine($"SandForge {CurrentVersion}"); return 0; }

    private static async Task<int?> TryAutomaticUpdateAsync(
        string[] args,
        UpdateService updater,
        UpdateSettingsStore settingsStore,
        CancellationToken cancellationToken)
    {
        if (args.Length > 0 && args[0].Equals("update", StringComparison.OrdinalIgnoreCase)) return null;
        if (args.Length > 0 && args[0] is "--help" or "-h" or "help" or "--version" or "version") return null;
        UpdateSettings settings = await settingsStore.LoadAsync(cancellationToken);
        if (!settingsStore.IsCheckDue(settings)) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            UpdateCheckResult check = await updater.CheckAsync(CurrentVersion, settings, timeout.Token);
            await settingsStore.RecordCheckAsync(cancellationToken);
            if (!check.IsUpdateAvailable) return null;
            if (settings.AutoApply && args.Length == 0)
            {
                UpdateApplyResult apply = await updater.ApplyAsync(check, cancellationToken);
                Console.WriteLine(apply.Message);
                return apply.Started ? 0 : null;
            }
            Console.Error.WriteLine($"[Update] {check.Message} sandforge update install");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Automatic update checks never break the requested command.
        }
        return null;
    }

    private static async Task<int> DoctorAsync(
        ISandboxBackend backend,
        SessionStore store,
        CacheService cache,
        UpdateSettingsStore updateSettingsStore,
        string dataDirectory,
        CancellationToken token)
    {
        SandboxAvailabilityResult result = await backend.CheckAvailabilityAsync(token);
        _ = await store.LoadAsync(token);
        UpdateSettings updates = await updateSettingsStore.LoadAsync(token);
        IReadOnlyList<CacheEntry> entries = await cache.ListAsync(token);
        Console.WriteLine("SANDFORGE DIAGNOSTICS\n" + new string('─', 52));
        Console.WriteLine($"[{(OperatingSystem.IsWindows() ? "OK" : "FAIL")}] Windows");
        Console.WriteLine($"[{(Environment.Is64BitOperatingSystem ? "OK" : "FAIL")}] 64-bit OS");
        Console.WriteLine($"[{(result.IsAvailable ? "OK" : "FAIL")}] {result.Message}");
        Console.WriteLine($"[OK] Data: {dataDirectory}");
        Console.WriteLine($"[OK] SQLite: {store.DatabasePath}");
        Console.WriteLine($"[OK] Managed cache: {FormatBytes(entries.Sum(x => x.SizeBytes))}");
        Console.WriteLine($"[OK] GitHub updates: {(updates.AutoCheck ? "auto-check" : "manual")}, {updates.Channel}");
        return result.IsAvailable ? 0 : 5;
    }

    private static async Task<int> RunTargetAsync(
        string[] args,
        SessionCoordinator coordinator,
        ReportWriter reports,
        string baseDirectory,
        string defaultTemplate,
        CancellationToken token)
    {
        if (args.Length < 2) return Unknown($"{args[0]} requires a file path");
        string template = ReadOption(args, "--template") ?? FindTemplate(baseDirectory, defaultTemplate);
        SessionRunResult result = await coordinator.RunAsync(template, args[1], token);
        Console.WriteLine(reports.ToConsole(result.Session));
        return ExitCode(result.Session.Status);
    }

    private static async Task<int> MatrixAsync(
        string[] args,
        SessionCoordinator coordinator,
        ReportWriter reports,
        string baseDirectory,
        CancellationToken token)
    {
        if (args.Length < 3 || !args[1].Equals("run", StringComparison.OrdinalIgnoreCase))
            return Unknown("matrix run <file> --templates <name1,name2>");
        string? templatesOption = ReadOption(args, "--templates");
        if (string.IsNullOrWhiteSpace(templatesOption)) return Unknown("matrix requires --templates");
        int parallel = ReadIntOption(args, "--parallel", 1, 1, 4);
        string[] templates = templatesOption.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (templates.Length == 0) return Unknown("matrix template list is empty");

        using var gate = new SemaphoreSlim(parallel, parallel);
        Task<MatrixItem>[] tasks = templates.Select(async templateName =>
        {
            await gate.WaitAsync(token);
            try
            {
                string templatePath = File.Exists(templateName) ? Path.GetFullPath(templateName) : FindTemplate(baseDirectory, templateName);
                SessionRunResult result = await coordinator.RunAsync(templatePath, args[2], token);
                return new MatrixItem(templateName, result, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new MatrixItem(templateName, null, exception.Message);
            }
            finally { gate.Release(); }
        }).ToArray();

        MatrixItem[] results = await Task.WhenAll(tasks);
        Console.WriteLine("MATRIX RUN\n" + new string('─', 70));
        foreach (MatrixItem item in results)
        {
            if (item.Result is null) Console.WriteLine($"{item.Template,-24} ERROR     {item.Error}");
            else Console.WriteLine($"{item.Template,-24} {StatusText(item.Result.Session.Status),-16} {item.Result.Session.Id}");
        }
        MatrixItem? detailed = results.FirstOrDefault(x => x.Result is not null);
        if (detailed?.Result is not null) Console.WriteLine("\n" + reports.ToConsole(detailed.Result.Session));
        return results.All(x => x.Result is not null && x.Result.Session.Status == SessionStatus.Completed) ? 0 : 9;
    }

    private static async Task<int> SessionAsync(string[] args, SessionStore store, ReportWriter reports, string dataDirectory, CancellationToken token)
    {
        if (args.Length < 2 || args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<SandboxSession> sessions = await store.LoadAsync(token);
            Console.WriteLine("ID                         TEMPLATE             STATUS             RISK       CLEANUP");
            foreach (SandboxSession s in sessions)
                Console.WriteLine($"{s.Id,-26} {s.TemplateId,-20} {StatusText(s.Status),-18} {RiskText(s.Risk),-10} {CleanupText(s.Cleanup)}");
            return 0;
        }
        if (args[1].Equals("show", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
        {
            SandboxSession? session = await store.FindAsync(args[2], token);
            if (session is null) return Unknown("session not found");
            Console.WriteLine(reports.ToConsole(session));
            return 0;
        }
        if (args[1].Equals("delete", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
        {
            SandboxSession? session = await store.FindAsync(args[2], token);
            if (session is null) return Unknown("session not found");
            string sessionsRoot = Path.Combine(dataDirectory, "sessions");
            if (!CleanupService.IsInside(sessionsRoot, session.WorkspacePath)) throw new InvalidDataException("Workspace path is outside the SandForge directory.");
            if (Directory.Exists(session.WorkspacePath)) Directory.Delete(session.WorkspacePath, true);
            await store.DeleteAsync(session.Id, token);
            Console.WriteLine(Text.Format("Sessions_Deleted", session.Id));
            return 0;
        }
        return Unknown("unsupported session command");
    }

    private static async Task<int> ReportAsync(string[] args, SessionStore store, ReportWriter reports, string dataDirectory, CancellationToken token)
    {
        if (args.Length < 2) return Unknown("report requires a session ID");
        SandboxSession? session = await store.FindAsync(args[1], token);
        if (session is null) return Unknown("session not found");
        string format = ReadOption(args, "--format") ?? "console";
        string directory = Path.Combine(dataDirectory, "reports", session.Id);
        Directory.CreateDirectory(directory);
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine(await reports.WriteJsonAsync(session, Path.Combine(directory, "report.json"), token));
        else if (format.Equals("html", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine(await reports.WriteHtmlAsync(session, Path.Combine(directory, "report.html"), token));
        else Console.WriteLine(reports.ToConsole(session));
        return 0;
    }

    private static async Task<int> RecoverAsync(SessionRecoveryService recovery, CancellationToken token)
    {
        RecoveryResult result = await recovery.RecoverAsync(token);
        Console.WriteLine(Text.Format("Recovery_Result", result.Inspected, result.Recovered, result.Orphaned, result.Failed));
        return result.Failed == 0 ? 0 : 9;
    }

    private static async Task<int> CleanupAsync(string[] args, CleanupService cleanup, CancellationToken token)
    {
        bool dry = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        bool orphaned = args.Contains("--orphaned", StringComparer.OrdinalIgnoreCase);
        TimeSpan age = ParseAge(ReadOption(args, "--older-than") ?? "30d");
        CleanupResult result = await cleanup.CleanupAsync(age, orphaned, dry, token);
        foreach (CleanupCandidate candidate in result.Candidates)
            Console.WriteLine($"{candidate.SessionId}  {StatusText(candidate.Status),-10}  {FormatBytes(candidate.SizeBytes),10}  {candidate.WorkspacePath}");
        Console.WriteLine(dry
            ? Text.Format("Cleanup_Preview", result.Candidates.Count, FormatBytes(result.Candidates.Sum(x => x.SizeBytes)))
            : Text.Format("Cleanup_Result", result.CleanedCount, FormatBytes(result.ReclaimedBytes)));
        return 0;
    }

    private static async Task<int> CacheAsync(string[] args, CacheService cache, CancellationToken token)
    {
        string action = args.Length > 1 ? args[1].ToLowerInvariant() : "list";
        if (action == "list")
        {
            IReadOnlyList<CacheEntry> entries = await cache.ListAsync(token);
            Console.WriteLine("TYPE      SIZE         PATH");
            foreach (CacheEntry entry in entries) Console.WriteLine($"{entry.Type,-9} {FormatBytes(entry.SizeBytes),10}  {entry.Path}");
            return 0;
        }
        if (action == "clean")
        {
            string? type = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null;
            bool dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
            CacheCleanupResult result = await cache.CleanupAsync(type, dryRun, token);
            Console.WriteLine(Text.Format("Cache_Result", result.RemovedEntries, FormatBytes(result.ReclaimedBytes)));
            return 0;
        }
        return Unknown("unsupported cache command");
    }

    private static async Task<int> UpdateAsync(string[] args, UpdateService updater, UpdateSettingsStore settingsStore, CancellationToken token)
    {
        string action = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
        UpdateSettings settings = await settingsStore.LoadAsync(token);
        if (action == "status")
        {
            Console.WriteLine($"Auto check: {settings.AutoCheck}");
            Console.WriteLine($"Auto apply: {settings.AutoApply}");
            Console.WriteLine($"Channel: {settings.Channel}");
            Console.WriteLine($"Interval: {settings.IntervalHours} h");
            Console.WriteLine($"GitHub repository: {settings.Repository}");
            return 0;
        }
        if (action == "check")
        {
            UpdateCheckResult check = await updater.CheckAsync(CurrentVersion, settings, token);
            await settingsStore.RecordCheckAsync(token);
            Console.WriteLine(check.Message);
            return check.IsUpdateAvailable ? 20 : 0;
        }
        if (action is "install" or "apply")
        {
            UpdateCheckResult check = await updater.CheckAsync(CurrentVersion, settings, token);
            await settingsStore.RecordCheckAsync(token);
            if (!check.IsUpdateAvailable) { Console.WriteLine(check.Message); return 0; }
            if (!args.Contains("--yes", StringComparer.OrdinalIgnoreCase))
            {
                Console.Write($"Install SandForge {check.LatestVersion}? [y/N] ");
                string? answer = Console.ReadLine();
                if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase)) return 0;
            }
            UpdateApplyResult apply = await updater.ApplyAsync(check, token);
            Console.WriteLine(apply.Message);
            return apply.Started ? 0 : 1;
        }
        if (action == "auto")
        {
            if (args.Length < 3) return Unknown("update auto requires on or off");
            bool enabled = args[2].Equals("on", StringComparison.OrdinalIgnoreCase);
            if (!enabled && !args[2].Equals("off", StringComparison.OrdinalIgnoreCase)) return Unknown("update auto requires on or off");
            settings = settings with
            {
                AutoCheck = enabled,
                AutoApply = enabled && args.Contains("--apply", StringComparer.OrdinalIgnoreCase)
            };
            await settingsStore.SaveAsync(settings, token);
            Console.WriteLine($"Auto check: {settings.AutoCheck}; auto apply: {settings.AutoApply}.");
            return 0;
        }
        if (action == "channel")
        {
            string channel = args.Length > 2 ? args[2].ToLowerInvariant() : string.Empty;
            if (channel is not ("stable" or "preview")) return Unknown("update channel requires stable or preview");
            settings = settings with { Channel = channel };
            await settingsStore.SaveAsync(settings, token);
            Console.WriteLine($"Update channel: {settings.Channel}.");
            return 0;
        }
        return Unknown("unsupported update command");
    }

    private static string? ReadOption(string[] args, string name)
    {
        int index = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int ReadIntOption(string[] args, string name, int fallback, int minimum, int maximum)
    {
        string? value = ReadOption(args, name);
        if (value is null) return fallback;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) || result < minimum || result > maximum)
            throw new InvalidDataException($"{name} must be between {minimum} and {maximum}.");
        return result;
    }

    private static string FindTemplate(string baseDirectory, string name)
    {
        string[] candidates =
        [
            Path.Combine(baseDirectory, "templates", name, "sandforge.yaml"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "templates", name, "sandforge.yaml")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "templates", name, "sandforge.yaml"))
        ];
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static TimeSpan ParseAge(string text)
    {
        text = text.Trim().ToLowerInvariant();
        if (text.EndsWith('d') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double days)) return TimeSpan.FromDays(days);
        if (text.EndsWith('h') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double hours)) return TimeSpan.FromHours(hours);
        throw new InvalidDataException("Age must be specified as 30d or 12h.");
    }

    private static int ExitCode(SessionStatus status) => status switch
    {
        SessionStatus.Completed => 0,
        SessionStatus.Partial => 9,
        SessionStatus.TimedOut => 11,
        _ => 10
    };

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double number = value;
        int index = 0;
        while (number >= 1024 && index < units.Length - 1) { number /= 1024; index++; }
        return $"{number.ToString("0.##", Text.Culture)} {units[index]}";
    }

    private static string StatusText(SessionStatus value) => Text.Status(value);
    private static string RiskText(RiskLevel value) => Text.Risk(value);
    private static string CleanupText(CleanupState value) => Text.Cleanup(value);

    private static int Unknown(string value)
    {
        Console.Error.WriteLine(Text.Format("Error_UnknownCommand", value));
        return 2;
    }

    private sealed record MatrixItem(string Template, SessionRunResult? Result, string? Error);
}
