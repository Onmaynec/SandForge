using System.Globalization;
using SandForge.Core;
using SandForge.Domain;
using SandForge.Reporting;
using SandForge.Sandbox;

return await SandForgeProgram.RunAsync(args);

internal static class SandForgeProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        string baseDirectory = AppContext.BaseDirectory;
        string dataDirectory = File.Exists(Path.Combine(baseDirectory, "portable.mode")) ? Path.Combine(baseDirectory, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SandForge");
        Directory.CreateDirectory(dataDirectory);

        var templateEngine = new TemplateEngine(); var security = new SecurityPolicyEngine(); var planner = new SessionPlanner(security);
        var workspace = new WorkspaceManager(); var generator = new SandboxConfigurationGenerator(); var backend = new WindowsSandboxBackend();
        var artifacts = new ArtifactManager(); var store = new SessionStore(dataDirectory); var reports = new ReportWriter();
        var coordinator = new SessionCoordinator(templateEngine, planner, workspace, generator, backend, artifacts, store, dataDirectory);
        var recovery = new SessionRecoveryService(store, artifacts); var cleanup = new CleanupService(store);

        try
        {
            if (args.Length == 0) return await InteractiveAsync(backend, store, reports, recovery, cleanup, dataDirectory, cts.Token);
            return args[0].ToLowerInvariant() switch
            {
                "--help" or "-h" or "help" => Help(), "--version" or "version" => Version(),
                "doctor" or "status" => await DoctorAsync(backend, store, dataDirectory, cts.Token),
                "run" => await RunTargetAsync(args, coordinator, reports, baseDirectory, "isolated-analysis", cts.Token),
                "run-script" => await RunTargetAsync(args, coordinator, reports, baseDirectory, "powershell-clean", cts.Token),
                "test-installer" => await RunTargetAsync(args, coordinator, reports, baseDirectory, "installer-test", cts.Token),
                "session" => await SessionAsync(args, store, reports, dataDirectory, cts.Token), "report" => await ReportAsync(args, store, reports, dataDirectory, cts.Token),
                "recover" => await RecoverAsync(recovery, cts.Token), "cleanup" => await CleanupAsync(args, cleanup, cts.Token), _ => Unknown(args[0])
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
          sandforge test-installer <файл>
          sandforge session list
          sandforge session show <id>
          sandforge session delete <id>
          sandforge report <id> [--format console|json|html]
          sandforge recover
          sandforge cleanup [--dry-run] [--older-than 30d] [--orphaned]
        """); return 0;
    }
    private static int Version() { Console.WriteLine("SandForge 0.2.0-alpha"); return 0; }

    private static async Task<int> DoctorAsync(ISandboxBackend backend, SessionStore store, string dataDirectory, CancellationToken token)
    {
        SandboxAvailabilityResult result = await backend.CheckAvailabilityAsync(token);
        _ = await store.LoadAsync(token);
        Console.WriteLine("ДИАГНОСТИКА SANDFORGE\n" + new string('─', 44));
        Console.WriteLine($"[{(OperatingSystem.IsWindows() ? "OK" : "FAIL")}] Windows");
        Console.WriteLine($"[{(Environment.Is64BitOperatingSystem ? "OK" : "FAIL")}] 64-разрядная ОС");
        Console.WriteLine($"[{(result.IsAvailable ? "OK" : "FAIL")}] {result.Message}");
        Console.WriteLine($"[OK] Каталог данных: {dataDirectory}"); Console.WriteLine($"[OK] SQLite: {store.DatabasePath}");
        return result.IsAvailable ? 0 : 5;
    }

    private static async Task<int> RunTargetAsync(string[] args, SessionCoordinator coordinator, ReportWriter reports, string baseDirectory, string defaultTemplate, CancellationToken token)
    {
        if (args.Length < 2) return Unknown($"команда {args[0]} требует путь к файлу");
        string template = ReadOption(args, "--template") ?? FindTemplate(baseDirectory, defaultTemplate);
        SessionRunResult result = await coordinator.RunAsync(template, args[1], token); Console.WriteLine(reports.ToConsole(result.Session));
        return result.Session.Status switch { SessionStatus.Completed => 0, SessionStatus.Partial => 9, SessionStatus.TimedOut => 11, _ => 10 };
    }

    private static async Task<int> SessionAsync(string[] args, SessionStore store, ReportWriter reports, string dataDirectory, CancellationToken token)
    {
        if (args.Length < 2 || args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<SandboxSession> sessions = await store.LoadAsync(token); Console.WriteLine("ID                         ШАБЛОН               СТАТУС       РИСК       ОЧИСТКА");
            foreach (SandboxSession s in sessions) Console.WriteLine($"{s.Id,-26} {s.TemplateId,-20} {StatusRu(s.Status),-18} {RiskRu(s.Risk),-10} {CleanupRu(s.Cleanup)}"); return 0;
        }
        if (args[1].Equals("show", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
        { SandboxSession? s = await store.FindAsync(args[2], token); if (s is null) return Unknown("сессия не найдена"); Console.WriteLine(reports.ToConsole(s)); return 0; }
        if (args[1].Equals("delete", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
        {
            SandboxSession? s = await store.FindAsync(args[2], token);
            if (s is null) return Unknown("сессия не найдена");
            string sessionsRoot = Path.Combine(dataDirectory, "sessions");
            if (!CleanupService.IsInside(sessionsRoot, s.WorkspacePath)) throw new InvalidDataException("Путь workspace находится вне каталога SandForge.");
            if (Directory.Exists(s.WorkspacePath)) Directory.Delete(s.WorkspacePath, true);
            await store.DeleteAsync(s.Id, token);
            Console.WriteLine($"Сессия {s.Id} удалена.");
            return 0;
        }
        return Unknown("неподдерживаемая команда session");
    }

    private static async Task<int> ReportAsync(string[] args, SessionStore store, ReportWriter reports, string dataDirectory, CancellationToken token)
    {
        if (args.Length < 2) return Unknown("команда report требует ID сессии"); SandboxSession? s = await store.FindAsync(args[1], token); if (s is null) return Unknown("сессия не найдена");
        string format = ReadOption(args, "--format") ?? "console"; string dir = Path.Combine(dataDirectory, "reports", s.Id); Directory.CreateDirectory(dir);
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase)) Console.WriteLine(await reports.WriteJsonAsync(s, Path.Combine(dir, "report.json"), token));
        else if (format.Equals("html", StringComparison.OrdinalIgnoreCase)) Console.WriteLine(await reports.WriteHtmlAsync(s, Path.Combine(dir, "report.html"), token)); else Console.WriteLine(reports.ToConsole(s)); return 0;
    }

    private static async Task<int> RecoverAsync(SessionRecoveryService recovery, CancellationToken token)
    { RecoveryResult r = await recovery.RecoverAsync(token); Console.WriteLine($"Проверено: {r.Inspected}; восстановлено: {r.Recovered}; orphaned: {r.Orphaned}; ошибок: {r.Failed}."); return r.Failed == 0 ? 0 : 9; }

    private static async Task<int> CleanupAsync(string[] args, CleanupService cleanup, CancellationToken token)
    {
        bool dry = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase); bool orphaned = args.Contains("--orphaned", StringComparer.OrdinalIgnoreCase);
        TimeSpan age = ParseAge(ReadOption(args, "--older-than") ?? "30d"); CleanupResult r = await cleanup.CleanupAsync(age, orphaned, dry, token);
        foreach (CleanupCandidate c in r.Candidates) Console.WriteLine($"{c.SessionId}  {c.Status,-10}  {FormatBytes(c.SizeBytes),10}  {c.WorkspacePath}");
        Console.WriteLine(dry ? $"Будет очищено: {r.Candidates.Count}." : $"Очищено: {r.CleanedCount}; освобождено: {FormatBytes(r.ReclaimedBytes)}."); return 0;
    }

    private static async Task<int> InteractiveAsync(ISandboxBackend backend, SessionStore store, ReportWriter reports, SessionRecoveryService recovery, CleanupService cleanup, string dataDirectory, CancellationToken token)
    {
        Console.WriteLine("╭────────────────────────────────────────╮\n│ SANDFORGE 0.2 — WINDOWS SANDBOX MANAGER │\n╰────────────────────────────────────────╯");
        Console.WriteLine("[1] Диагностика\n[2] История сессий\n[3] Восстановить сессии\n[4] Предпросмотр очистки\n[0] Выход"); Console.Write("> ");
        return Console.ReadLine() switch { "1" => await DoctorAsync(backend, store, dataDirectory, token), "2" => await SessionAsync(["session", "list"], store, reports, dataDirectory, token), "3" => await RecoverAsync(recovery, token), "4" => await CleanupAsync(["cleanup", "--dry-run"], cleanup, token), _ => 0 };
    }

    private static string? ReadOption(string[] args, string name) { int i = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    private static string FindTemplate(string baseDirectory, string name)
    {
        string[] c = [Path.Combine(baseDirectory, "templates", name, "sandforge.yaml"), Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "templates", name, "sandforge.yaml")), Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "templates", name, "sandforge.yaml"))];
        return c.FirstOrDefault(File.Exists) ?? c[0];
    }
    private static TimeSpan ParseAge(string text)
    {
        text = text.Trim().ToLowerInvariant();
        if (text.EndsWith('d') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double days)) return TimeSpan.FromDays(days);
        if (text.EndsWith('h') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double hours)) return TimeSpan.FromHours(hours);
        throw new InvalidDataException("Возраст должен быть задан как 30d или 12h.");
    }
    private static string FormatBytes(long value) { string[] units = ["B", "KB", "MB", "GB"]; double n = value; int i = 0; while (n >= 1024 && i < units.Length - 1) { n /= 1024; i++; } return $"{n:0.##} {units[i]}"; }

    private static string StatusRu(SessionStatus value) => value switch
    {
        SessionStatus.Created => "Создана", SessionStatus.Validating => "Проверка", SessionStatus.Preparing => "Подготовка",
        SessionStatus.Ready => "Готова", SessionStatus.Starting => "Запуск", SessionStatus.Running => "Работает",
        SessionStatus.Stopping => "Остановка", SessionStatus.Collecting => "Сбор данных", SessionStatus.Completed => "Завершена",
        SessionStatus.Partial => "Частично", SessionStatus.Failed => "Ошибка", SessionStatus.Cancelled => "Отменена",
        SessionStatus.TimedOut => "Timeout", SessionStatus.Orphaned => "Потеряна", _ => value.ToString()
    };
    private static string RiskRu(RiskLevel value) => value switch { RiskLevel.Low => "Низкий", RiskLevel.Medium => "Средний", RiskLevel.High => "Высокий", RiskLevel.Critical => "Критический", _ => value.ToString() };
    private static string CleanupRu(CleanupState value) => value switch { CleanupState.Pending => "Ожидает", CleanupState.Kept => "Сохранён", CleanupState.Cleaned => "Очищен", _ => value.ToString() };

    private static int Unknown(string value) { Console.Error.WriteLine($"Неверная команда: {value}. Используйте sandforge --help."); return 2; }
}
