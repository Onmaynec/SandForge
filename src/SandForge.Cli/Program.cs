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
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cts.Cancel(); };

        string baseDirectory = AppContext.BaseDirectory;
        string dataDirectory = File.Exists(Path.Combine(baseDirectory, "portable.mode"))
            ? Path.Combine(baseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SandForge");
        Directory.CreateDirectory(dataDirectory);

        var templateEngine = new TemplateEngine();
        var security = new SecurityPolicyEngine();
        var planner = new SessionPlanner(security);
        var workspace = new WorkspaceManager();
        var generator = new SandboxConfigurationGenerator();
        var backend = new WindowsSandboxBackend();
        var artifacts = new ArtifactManager();
        var store = new SessionStore(dataDirectory);
        var reports = new ReportWriter();
        var coordinator = new SessionCoordinator(templateEngine, planner, workspace, generator, backend, artifacts, store, dataDirectory);

        try
        {
            if (args.Length == 0) return await InteractiveAsync(backend, store, reports, cts.Token);
            return args[0].ToLowerInvariant() switch
            {
                "--help" or "-h" or "help" => Help(),
                "--version" or "version" => Version(),
                "doctor" or "status" => await DoctorAsync(backend, dataDirectory, cts.Token),
                "run" => await RunTargetAsync(args, coordinator, reports, baseDirectory, cts.Token),
                "run-script" => await RunScriptAsync(args, coordinator, reports, baseDirectory, cts.Token),
                "session" => await SessionAsync(args, store, reports, cts.Token),
                "report" => await ReportAsync(args, store, reports, dataDirectory, cts.Token),
                _ => Unknown(args[0])
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 12;
        }
        catch (FileNotFoundException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 3;
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 4;
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("Security policy", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(exception.Message);
            return 7;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SandForge error: {exception.Message}");
            return 1;
        }
    }

    private static int Help()
    {
        Console.WriteLine("""
        SandForge — Forge disposable Windows environments.

        Usage:
          sandforge doctor
          sandforge run <file> [--template <path>]
          sandforge run-script <file> [--template <path>]
          sandforge session list
          sandforge session show <id>
          sandforge report <id> [--format console|json|html]

        The MVP defaults to templates/isolated-analysis/sandforge.yaml.
        """);
        return 0;
    }

    private static int Version()
    {
        Console.WriteLine("SandForge 0.1.0-alpha");
        return 0;
    }

    private static async Task<int> DoctorAsync(ISandboxBackend backend, string dataDirectory, CancellationToken cancellationToken)
    {
        SandboxAvailabilityResult result = await backend.CheckAvailabilityAsync(cancellationToken);
        Console.WriteLine("SANDFORGE DOCTOR");
        Console.WriteLine(new string('─', 40));
        Console.WriteLine($"[{(OperatingSystem.IsWindows() ? "OK" : "FAIL")}] Windows host");
        Console.WriteLine($"[{(Environment.Is64BitOperatingSystem ? "OK" : "FAIL")}] x64 operating system");
        Console.WriteLine($"[{(result.IsAvailable ? "OK" : "FAIL")}] {result.Message}");
        Console.WriteLine($"[OK] Data directory: {dataDirectory}");
        return result.IsAvailable ? 0 : 5;
    }

    private static async Task<int> RunTargetAsync(string[] args, SessionCoordinator coordinator, ReportWriter reports, string baseDirectory, CancellationToken cancellationToken)
    {
        if (args.Length < 2) return Unknown("run requires a file path");
        string template = ReadOption(args, "--template") ?? FindTemplate(baseDirectory, "isolated-analysis");
        SessionRunResult result = await coordinator.RunAsync(template, args[1], cancellationToken);
        Console.WriteLine(reports.ToConsole(result.Session));
        return result.Session.Status switch
        {
            SessionStatus.Completed => 0,
            SessionStatus.Partial => 9,
            SessionStatus.TimedOut => 11,
            _ => 10
        };
    }

    private static async Task<int> RunScriptAsync(string[] args, SessionCoordinator coordinator, ReportWriter reports, string baseDirectory, CancellationToken cancellationToken)
    {
        if (args.Length < 2) return Unknown("run-script requires a file path");
        string template = ReadOption(args, "--template") ?? FindTemplate(baseDirectory, "powershell-clean");
        SessionRunResult result = await coordinator.RunAsync(template, args[1], cancellationToken);
        Console.WriteLine(reports.ToConsole(result.Session));
        return result.Session.Status == SessionStatus.Completed ? 0 : 10;
    }

    private static async Task<int> SessionAsync(string[] args, SessionStore store, ReportWriter reports, CancellationToken cancellationToken)
    {
        if (args.Length < 2 || args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<SandboxSession> sessions = await store.LoadAsync(cancellationToken);
            Console.WriteLine("ID                         TEMPLATE             STATUS       RISK");
            foreach (SandboxSession session in sessions)
                Console.WriteLine($"{session.Id,-26} {session.TemplateId,-20} {session.Status,-12} {session.Risk}");
            return 0;
        }
        if (args[1].Equals("show", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
        {
            SandboxSession? session = await store.FindAsync(args[2], cancellationToken);
            if (session is null) return Unknown("session not found");
            Console.WriteLine(reports.ToConsole(session));
            return 0;
        }
        return Unknown("unsupported session command");
    }

    private static async Task<int> ReportAsync(string[] args, SessionStore store, ReportWriter reports, string dataDirectory, CancellationToken cancellationToken)
    {
        if (args.Length < 2) return Unknown("report requires a session id");
        SandboxSession? session = await store.FindAsync(args[1], cancellationToken);
        if (session is null) return Unknown("session not found");
        string format = ReadOption(args, "--format") ?? "console";
        string reportDirectory = Path.Combine(dataDirectory, "reports", session.Id);
        Directory.CreateDirectory(reportDirectory);
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine(await reports.WriteJsonAsync(session, Path.Combine(reportDirectory, "report.json"), cancellationToken));
        else if (format.Equals("html", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine(await reports.WriteHtmlAsync(session, Path.Combine(reportDirectory, "report.html"), cancellationToken));
        else
            Console.WriteLine(reports.ToConsole(session));
        return 0;
    }

    private static async Task<int> InteractiveAsync(ISandboxBackend backend, SessionStore store, ReportWriter reports, CancellationToken cancellationToken)
    {
        Console.WriteLine("╭──────────────────────────────────────╮");
        Console.WriteLine("│ SANDFORGE                            │");
        Console.WriteLine("│ Forge disposable Windows environments│");
        Console.WriteLine("╰──────────────────────────────────────╯");
        Console.WriteLine("[1] Диагностика\n[2] История сессий\n[0] Выход");
        Console.Write("> ");
        string? choice = Console.ReadLine();
        if (choice == "1") return await DoctorAsync(backend, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SandForge"), cancellationToken);
        if (choice == "2") return await SessionAsync(["session", "list"], store, reports, cancellationToken);
        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        int index = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
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

    private static int Unknown(string value)
    {
        Console.Error.WriteLine($"Invalid command: {value}. Use sandforge --help.");
        return 2;
    }
}
