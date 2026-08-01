using System.Globalization;
using System.Text.Json;
using SandForge.Core;
using SandForge.Domain;
using SandForge.Reporting;
using SandForge.Sandbox;

return await SandForgeProgram.RunAsync(args);

internal static class SandForgeProgram
{
    private const string CurrentVersion = "0.3.0-alpha";

    public static async Task<int> RunAsync(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        string baseDirectory = AppContext.BaseDirectory;
        string dataDirectory = File.Exists(Path.Combine(baseDirectory, "portable.mode"))
            ? Path.Combine(baseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SandForge");
        Directory.CreateDirectory(dataDirectory);

        var templateEngine = new TemplateEngine();
        var security = new SecurityPolicyEngine();
        var planner = new SessionPlanner(security, dataDirectory);
        var workspace = new WorkspaceManager();
        var generator = new SandboxConfigurationGenerator();
        var backend = new WindowsSandboxBackend();
        var artifacts = new ArtifactManager();
        var store = new SessionStore(dataDirectory);
        var reports = new ReportWriter();
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
                return await InteractiveAsync(backend, store, reports, recovery, cleanup, cache, updater, updateSettingsStore, dataDirectory, cts.Token);

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
        catch (OperationCanceledException) { Console.Error.WriteLine("Операция отменена."); return 12; }
        catch (FileNotFoundException e) { Console.Error.WriteLine(e.Message); return 3; }
        catch (InvalidDataException e) { Console.Error.WriteLine(e.Message); return 4; }
        catch (InvalidOperationException e) when (e.Message.Contains("безопасност", StringComparison.OrdinalIgnoreCase)) { Console.Error.WriteLine(e.Message); return 7; }
        catch (Exception e) { Console.Error.WriteLine($"Ошибка SandForge: {e.Message}"); return 1; }
    }

    private static int Help()
    {
        Console.WriteLine("""
        SandForge — менеджер одноразовых Windows-окружений.

        Использование:
          sandforge doctor
          sandforge run <файл> [--template <путь>]
          sandforge run-script <файл> [--template <путь>]
          sandforge test-installer <файл> [--template <путь>]
          sandforge matrix run <файл> --templates <имя1,имя2> [--parallel 2]
          sandforge session list|show|delete [id]
          sandforge report <id> [--format console|json|html]
          sandforge recover
          sandforge cleanup [--dry-run] [--older-than 30d] [--orphaned]
          sandforge cache list
          sandforge cache clean [тип] [--dry-run]
          sandforge update check
          sandforge update install [--yes]
          sandforge update auto on|off [--apply]
          sandforge update channel stable|preview

        Шаблоны schemaVersion 2 поддерживают extends/includes, provisioning и managed cache.
        """);
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
            Console.Error.WriteLine($"[Обновление] {check.Message} Запустите: sandforge update install");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Автопроверка не должна ломать основную команду.
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
        Console.WriteLine("ДИАГНОСТИКА SANDFORGE\n" + new string('─', 52));
        Console.WriteLine($"[{(OperatingSystem.IsWindows() ? "OK" : "FAIL")}] Windows");
        Console.WriteLine($"[{(Environment.Is64BitOperatingSystem ? "OK" : "FAIL")}] 64-разрядная ОС");
        Console.WriteLine($"[{(result.IsAvailable ? "OK" : "FAIL")}] {result.Message}");
        Console.WriteLine($"[OK] Каталог данных: {dataDirectory}");
        Console.WriteLine($"[OK] SQLite: {store.DatabasePath}");
        Console.WriteLine($"[OK] Managed cache: {FormatBytes(entries.Sum(x => x.SizeBytes))}");
        Console.WriteLine($"[OK] GitHub updates: {(updates.AutoCheck ? "auto-check" : "manual")}, канал {updates.Channel}");
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
        if (args.Length < 2) return Unknown($"команда {args[0]} требует путь к файлу");
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
            return Unknown("использование: matrix run <файл> --templates <имя1,имя2>");
        string? templatesOption = ReadOption(args, "--templates");
        if (string.IsNullOrWhiteSpace(templatesOption)) return Unknown("matrix требует --templates");
        int parallel = ReadIntOption(args, "--parallel", 1, 1, 4);
        string[] templates = templatesOption.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (templates.Length == 0) return Unknown("список matrix templates пуст");

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
            if (item.Result is null) Console.WriteLine($"{item.Template,-24} ОШИБКА   {item.Error}");
            else Console.WriteLine($"{item.Template,-24} {StatusRu(item.Result.Session.Status),-16} {item.Result.Session.Id}");
        }
        MatrixItem? detailed = results.FirstOrDefault(x => x.Result is not null);
        if (detailed?.Result is not null) Console.WriteLine("\nПервый отчёт:\n" + reports.ToConsole(detailed.Result.Session));
        return results.All(x => x.Result is not null && x.Result.Session.Status == SessionStatus.Completed) ? 0 : 9;
    }

    private static async Task<int> SessionAsync(string[] args, SessionStore store, ReportWriter reports, string dataDirectory, CancellationToken token)
    {
        if (args.Length < 2 || args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<SandboxSession> sessions = await store.LoadAsync(token);
            Console.WriteLine("ID                         ШАБЛОН               СТАТУС       РИСК       ОЧИСТКА");
            foreach (SandboxSession s in sessions)
                Console.WriteLine($"{s.Id,-26} {s.TemplateId,-20} {StatusRu(s.Status),-18} {RiskRu(s.Risk),-10} {CleanupRu(s.Cleanup)}");
            return 0;
        }
        if (args[1].Equals("show", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
        {
            SandboxSession? session = await store.FindAsync(args[2], token);
            if (session is null) return Unknown("сессия не найдена");
            Console.WriteLine(reports.ToConsole(session));
            return 0;
        }
        if (args[1].Equals("delete", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
        {
            SandboxSession? session = await store.FindAsync(args[2], token);
            if (session is null) return Unknown("сессия не найдена");
            string sessionsRoot = Path.Combine(dataDirectory, "sessions");
            if (!CleanupService.IsInside(sessionsRoot, session.WorkspacePath)) throw new InvalidDataException("Путь workspace находится вне каталога SandForge.");
            if (Directory.Exists(session.WorkspacePath)) Directory.Delete(session.WorkspacePath, true);
            await store.DeleteAsync(session.Id, token);
            Console.WriteLine($"Сессия {session.Id} удалена.");
            return 0;
        }
        return Unknown("неподдерживаемая команда session");
    }

    private static async Task<int> ReportAsync(string[] args, SessionStore store, ReportWriter reports, string dataDirectory, CancellationToken token)
    {
        if (args.Length < 2) return Unknown("команда report требует ID сессии");
        SandboxSession? session = await store.FindAsync(args[1], token);
        if (session is null) return Unknown("сессия не найдена");
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
        Console.WriteLine($"Проверено: {result.Inspected}; восстановлено: {result.Recovered}; orphaned: {result.Orphaned}; ошибок: {result.Failed}.");
        return result.Failed == 0 ? 0 : 9;
    }

    private static async Task<int> CleanupAsync(string[] args, CleanupService cleanup, CancellationToken token)
    {
        bool dry = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        bool orphaned = args.Contains("--orphaned", StringComparer.OrdinalIgnoreCase);
        TimeSpan age = ParseAge(ReadOption(args, "--older-than") ?? "30d");
        CleanupResult result = await cleanup.CleanupAsync(age, orphaned, dry, token);
        foreach (CleanupCandidate candidate in result.Candidates)
            Console.WriteLine($"{candidate.SessionId}  {candidate.Status,-10}  {FormatBytes(candidate.SizeBytes),10}  {candidate.WorkspacePath}");
        Console.WriteLine(dry ? $"Будет очищено: {result.Candidates.Count}." : $"Очищено: {result.CleanedCount}; освобождено: {FormatBytes(result.ReclaimedBytes)}.");
        return 0;
    }

    private static async Task<int> CacheAsync(string[] args, CacheService cache, CancellationToken token)
    {
        string action = args.Length > 1 ? args[1].ToLowerInvariant() : "list";
        if (action == "list")
        {
            IReadOnlyList<CacheEntry> entries = await cache.ListAsync(token);
            Console.WriteLine("ТИП       РАЗМЕР       ПУТЬ");
            foreach (CacheEntry entry in entries) Console.WriteLine($"{entry.Type,-9} {FormatBytes(entry.SizeBytes),10}  {entry.Path}");
            return 0;
        }
        if (action == "clean")
        {
            string? type = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null;
            bool dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
            CacheCleanupResult result = await cache.CleanupAsync(type, dryRun, token);
            Console.WriteLine(dryRun
                ? $"Будет удалено файлов: {result.RemovedEntries}, объём: {FormatBytes(result.ReclaimedBytes)}."
                : $"Удалено файлов: {result.RemovedEntries}, освобождено: {FormatBytes(result.ReclaimedBytes)}.");
            return 0;
        }
        return Unknown("неподдерживаемая команда cache");
    }

    private static async Task<int> UpdateAsync(string[] args, UpdateService updater, UpdateSettingsStore settingsStore, CancellationToken token)
    {
        string action = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
        UpdateSettings settings = await settingsStore.LoadAsync(token);
        if (action == "status")
        {
            Console.WriteLine($"Автопроверка: {(settings.AutoCheck ? "включена" : "выключена")}");
            Console.WriteLine($"Автоустановка: {(settings.AutoApply ? "включена" : "выключена")}");
            Console.WriteLine($"Канал: {settings.Channel}");
            Console.WriteLine($"Интервал: {settings.IntervalHours} ч.");
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
                Console.Write($"Установить SandForge {check.LatestVersion}? [y/N] ");
                string? answer = Console.ReadLine();
                if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase)) return 0;
            }
            UpdateApplyResult apply = await updater.ApplyAsync(check, token);
            Console.WriteLine(apply.Message);
            return apply.Started ? 0 : 1;
        }
        if (action == "auto")
        {
            if (args.Length < 3) return Unknown("update auto требует on или off");
            bool enabled = args[2].Equals("on", StringComparison.OrdinalIgnoreCase);
            if (!enabled && !args[2].Equals("off", StringComparison.OrdinalIgnoreCase)) return Unknown("update auto требует on или off");
            settings = settings with
            {
                AutoCheck = enabled,
                AutoApply = enabled && args.Contains("--apply", StringComparer.OrdinalIgnoreCase)
            };
            await settingsStore.SaveAsync(settings, token);
            Console.WriteLine(enabled
                ? $"Автообновления включены{(settings.AutoApply ? " с автоматической установкой при запуске меню" : " в режиме уведомлений")} ."
                : "Автообновления выключены.");
            return 0;
        }
        if (action == "channel")
        {
            string channel = args.Length > 2 ? args[2].ToLowerInvariant() : string.Empty;
            if (channel is not ("stable" or "preview")) return Unknown("update channel требует stable или preview");
            settings = settings with { Channel = channel };
            await settingsStore.SaveAsync(settings, token);
            Console.WriteLine($"Канал обновлений: {settings.Channel}.");
            return 0;
        }
        return Unknown("неподдерживаемая команда update");
    }

    private static async Task<int> InteractiveAsync(
        ISandboxBackend backend,
        SessionStore store,
        ReportWriter reports,
        SessionRecoveryService recovery,
        CleanupService cleanup,
        CacheService cache,
        UpdateService updater,
        UpdateSettingsStore updateSettingsStore,
        string dataDirectory,
        CancellationToken token)
    {
        Console.WriteLine("╭──────────────────────────────────────────╮\n│ SANDFORGE 0.3 — WINDOWS SANDBOX MANAGER │\n╰──────────────────────────────────────────╯");
        Console.WriteLine("[1] Диагностика\n[2] История сессий\n[3] Восстановить сессии\n[4] Предпросмотр очистки\n[5] Managed cache\n[6] Проверить обновления\n[0] Выход");
        Console.Write("> ");
        return Console.ReadLine() switch
        {
            "1" => await DoctorAsync(backend, store, cache, updateSettingsStore, dataDirectory, token),
            "2" => await SessionAsync(["session", "list"], store, reports, dataDirectory, token),
            "3" => await RecoverAsync(recovery, token),
            "4" => await CleanupAsync(["cleanup", "--dry-run"], cleanup, token),
            "5" => await CacheAsync(["cache", "list"], cache, token),
            "6" => await UpdateAsync(["update", "check"], updater, updateSettingsStore, token),
            _ => 0
        };
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
            throw new InvalidDataException($"{name} должен быть числом от {minimum} до {maximum}.");
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
        throw new InvalidDataException("Возраст должен быть задан как 30d или 12h.");
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
        return $"{number:0.##} {units[index]}";
    }

    private static string StatusRu(SessionStatus value) => value switch
    {
        SessionStatus.Created => "Создана", SessionStatus.Validating => "Проверка", SessionStatus.Preparing => "Подготовка",
        SessionStatus.Ready => "Готова", SessionStatus.Starting => "Запуск", SessionStatus.Running => "Работает",
        SessionStatus.Stopping => "Остановка", SessionStatus.Collecting => "Сбор данных", SessionStatus.Completed => "Завершена",
        SessionStatus.Partial => "Частично", SessionStatus.Failed => "Ошибка", SessionStatus.Cancelled => "Отменена",
        SessionStatus.TimedOut => "Timeout", SessionStatus.Orphaned => "Потеряна", _ => value.ToString()
    };

    private static string RiskRu(RiskLevel value) => value switch
    {
        RiskLevel.Low => "Низкий", RiskLevel.Medium => "Средний", RiskLevel.High => "Высокий", RiskLevel.Critical => "Критический", _ => value.ToString()
    };

    private static string CleanupRu(CleanupState value) => value switch
    {
        CleanupState.Pending => "Ожидает", CleanupState.Kept => "Сохранён", CleanupState.Cleaned => "Очищен", _ => value.ToString()
    };

    private static int Unknown(string value)
    {
        Console.Error.WriteLine($"Неверная команда: {value}. Используйте sandforge --help.");
        return 2;
    }

    private sealed record MatrixItem(string Template, SessionRunResult? Result, string? Error);
}
