using System.Globalization;
using SandForge.Core;
using SandForge.Domain;
using SandForge.Reporting;
using SandForge.Sandbox;
using Spectre.Console;

namespace SandForge.Cli;

internal sealed class TuiApplication
{
    private readonly TemplateEngine _templateEngine;
    private readonly SessionPlanner _planner;
    private readonly SessionCoordinator _coordinator;
    private readonly ISandboxBackend _backend;
    private readonly SessionStore _store;
    private readonly ReportWriter _reports;
    private readonly SessionRecoveryService _recovery;
    private readonly CleanupService _cleanup;
    private readonly CacheService _cache;
    private readonly UpdateService _updater;
    private readonly UpdateSettingsStore _updateSettingsStore;
    private readonly string _dataDirectory;
    private readonly string _baseDirectory;
    private readonly UiText _text;
    private readonly bool _animations;

    public TuiApplication(
        TemplateEngine templateEngine,
        SessionPlanner planner,
        SessionCoordinator coordinator,
        ISandboxBackend backend,
        SessionStore store,
        ReportWriter reports,
        SessionRecoveryService recovery,
        CleanupService cleanup,
        CacheService cache,
        UpdateService updater,
        UpdateSettingsStore updateSettingsStore,
        string dataDirectory,
        string baseDirectory,
        UiText text,
        bool animations)
    {
        _templateEngine = templateEngine;
        _planner = planner;
        _coordinator = coordinator;
        _backend = backend;
        _store = store;
        _reports = reports;
        _recovery = recovery;
        _cleanup = cleanup;
        _cache = cache;
        _updater = updater;
        _updateSettingsStore = updateSettingsStore;
        _dataDirectory = dataDirectory;
        _baseDirectory = baseDirectory;
        _text = text;
        _animations = animations;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnsiConsole.Clear();
            await RenderDashboardAsync(cancellationToken);

            TuiAction action = AnsiConsole.Prompt(
                new SelectionPrompt<TuiAction>()
                    .Title($"[bold cyan]{Markup.Escape(_text["Dashboard_Title"])}[/]")
                    .PageSize(10)
                    .UseConverter(ActionLabel)
                    .AddChoices(Enum.GetValues<TuiAction>()));

            if (action == TuiAction.Exit) return 0;
            try
            {
                switch (action)
                {
                    case TuiAction.Run: await RunWizardAsync("isolated-analysis", cancellationToken); break;
                    case TuiAction.TestInstaller: await RunWizardAsync("installer-test", cancellationToken); break;
                    case TuiAction.Sessions: await ShowSessionsAsync(cancellationToken); break;
                    case TuiAction.Recovery: await ShowRecoveryAsync(cancellationToken); break;
                    case TuiAction.Cleanup: await ShowCleanupAsync(cancellationToken); break;
                    case TuiAction.Cache: await ShowCacheAsync(cancellationToken); break;
                    case TuiAction.Updates: await ShowUpdatesAsync(cancellationToken); break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(_text.Format("Error_Generic", exception.Message))}[/]");
            }
            Pause();
        }
    }

    private async Task RenderDashboardAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.Write(new FigletText(_text["App_Title"]).Color(Color.Cyan1));
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(_text["App_Subtitle"])}[/]");
        AnsiConsole.WriteLine();

        SandboxAvailabilityResult availability = await _backend.CheckAvailabilityAsync(cancellationToken);
        IReadOnlyList<SandboxSession> sessions = await _store.LoadAsync(cancellationToken);

        var environment = new Table().Border(TableBorder.Rounded).Expand();
        environment.Title = new TableTitle(_text["Dashboard_Environment"]);
        environment.AddColumn("Component");
        environment.AddColumn("Status");
        environment.AddRow("Windows", OperatingSystem.IsWindows() ? "[green]OK[/]" : "[red]FAIL[/]");
        environment.AddRow("Windows Sandbox", availability.IsAvailable
            ? $"[green]{Markup.Escape(availability.Message)}[/]"
            : $"[red]{Markup.Escape(availability.Message)}[/]");
        environment.AddRow("SQLite", $"[green]{Markup.Escape(_store.DatabasePath)}[/]");
        environment.AddRow("Language", $"[cyan]{_text.LanguageCode}[/]");
        environment.AddRow("Data", Markup.Escape(_dataDirectory));
        AnsiConsole.Write(environment);
        AnsiConsole.WriteLine();

        var recent = new Table().Border(TableBorder.Simple).Expand();
        recent.Title = new TableTitle(_text["Dashboard_RecentSessions"]);
        recent.AddColumn("ID");
        recent.AddColumn(_text["Report_Template"]);
        recent.AddColumn(_text["Report_Status"]);
        recent.AddColumn(_text["Report_Risk"]);
        foreach (SandboxSession session in sessions.Take(5))
        {
            recent.AddRow(
                Markup.Escape(session.Id),
                Markup.Escape(session.TemplateId),
                Markup.Escape(_text.Status(session.Status)),
                RiskMarkup(session.Risk));
        }
        if (sessions.Count == 0)
            recent.AddRow("[grey]—[/]", "[grey]—[/]", $"[grey]{Markup.Escape(_text["Dashboard_NoSessions"])}[/]", "[grey]—[/]");
        AnsiConsole.Write(recent);
        AnsiConsole.WriteLine();
    }

    private async Task RunWizardAsync(string preferredTemplate, CancellationToken cancellationToken)
    {
        string targetPath = AnsiConsole.Prompt(
            new TextPrompt<string>($"[cyan]{Markup.Escape(_text["Prompt_TargetPath"])}[/]")
                .ValidationErrorMessage("[red]File not found[/]")
                .Validate(path => File.Exists(Environment.ExpandEnvironmentVariables(path.Trim(' ', '"')))
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]File not found[/]")));
        targetPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(targetPath.Trim(' ', '"')));

        IReadOnlyList<TemplateChoice> templates = await DiscoverTemplatesAsync(cancellationToken);
        if (templates.Count == 0) throw new FileNotFoundException("SandForge templates were not found.");
        TemplateChoice preferred = templates.FirstOrDefault(x => x.Name.Equals(preferredTemplate, StringComparison.OrdinalIgnoreCase))
            ?? templates[0];
        TemplateChoice selected = AnsiConsole.Prompt(
            new SelectionPrompt<TemplateChoice>()
                .Title($"[cyan]{Markup.Escape(_text["Prompt_Template"])}[/]")
                .UseConverter(x => x.DisplayName)
                .AddChoices([preferred, .. templates.Where(x => !x.Path.Equals(preferred.Path, StringComparison.OrdinalIgnoreCase))]));

        TemplateDefinition template = await _templateEngine.LoadAsync(selected.Path, cancellationToken);
        SessionPlan plan = await _planner.CreateAsync(template, targetPath, cancellationToken);
        RenderSecurityPlan(plan);
        if (plan.Security.IsBlocked)
        {
            AnsiConsole.MarkupLine($"[bold red]{Markup.Escape(_text["Security_Blocked"])}[/]");
            return;
        }

        bool weakenedIsolation = plan.Security.Risk is RiskLevel.High or RiskLevel.Critical
            || plan.Sandbox.Network != NetworkPolicy.Disabled
            || plan.Sandbox.Clipboard != ClipboardPolicy.Disabled
            || plan.Mounts.Any(x => x.Mode == MountMode.ReadWrite);
        string confirmation = weakenedIsolation ? _text["Prompt_ConfirmDangerousLaunch"] : _text["Prompt_ConfirmLaunch"];
        if (!AnsiConsole.Prompt(new ConfirmationPrompt(Markup.Escape(confirmation)) { DefaultValue = false })) return;

        SessionRunResult? runResult = null;
        await AnsiConsole.Status()
            .Spinner(_animations ? Spinner.Known.Dots : Spinner.Known.Ascii)
            .StartAsync(_text["Run_Validating"], async context =>
            {
                var progress = new DelegateProgress<SessionProgress>(value => context.Status(ProgressText(value)));
                runResult = await _coordinator.RunAsync(selected.Path, targetPath, progress, cancellationToken);
            });

        if (runResult is null) return;
        AnsiConsole.Write(new Rule($"[cyan]{Markup.Escape(_text["Run_Completed"])}[/]"));
        AnsiConsole.Write(new Panel(new Text(_reports.ToConsole(runResult.Session))).Expand());
        RenderArtifacts(runResult.Session);
        await OfferReportAsync(runResult.Session, cancellationToken);
    }

    private void RenderSecurityPlan(SessionPlan plan)
    {
        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.Title = new TableTitle(_text["Security_Title"]);
        table.AddColumn(_text["Security_Title"]);
        table.AddColumn("Value");
        table.AddRow(_text["Security_Risk"], RiskMarkup(plan.Security.Risk));
        table.AddRow(_text["Security_Network"], Markup.Escape(_text.Network(plan.Sandbox.Network)));
        table.AddRow(_text["Security_Clipboard"], Markup.Escape(_text.Clipboard(plan.Sandbox.Clipboard)));
        table.AddRow(_text["Security_Timeout"], Markup.Escape(plan.Session.Timeout.ToString()));
        table.AddRow(_text["Security_Mounts"], plan.Mounts.Count.ToString(_text.Culture));
        table.AddRow(_text["Security_Collectors"], Markup.Escape(string.Join(", ", plan.ArtifactCollectors)));
        AnsiConsole.Write(table);

        if (plan.Security.Findings.Count == 0)
        {
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(_text["Security_NoFindings"])}[/]");
            return;
        }
        foreach (SecurityFinding finding in plan.Security.Findings)
        {
            string color = finding.Level switch
            {
                RiskLevel.Critical => "red bold",
                RiskLevel.High => "red",
                RiskLevel.Medium => "yellow",
                _ => "grey"
            };
            AnsiConsole.MarkupLine($"[{color}]• {Markup.Escape(finding.Code)}: {Markup.Escape(finding.Message)}[/]");
        }
    }

    private async Task ShowSessionsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<SandboxSession> sessions = await _store.LoadAsync(cancellationToken);
            AnsiConsole.Write(new Rule($"[cyan]{Markup.Escape(_text["Sessions_Title"])}[/]"));
            if (sessions.Count == 0)
            {
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(_text["Sessions_Empty"])}[/]");
                return;
            }

            SessionChoice selectedChoice = AnsiConsole.Prompt(
                new SelectionPrompt<SessionChoice>()
                    .Title($"[cyan]{Markup.Escape(_text["Prompt_SelectSession"])}[/]")
                    .PageSize(12)
                    .UseConverter(choice => choice.Session is null
                        ? $"← {_text["Sessions_ActionBack"]}"
                        : $"{choice.Session.CreatedAt.ToLocalTime().ToString("g", _text.Culture)}  {choice.Session.Id}  {_text.Status(choice.Session.Status)}")
                    .AddChoices([.. sessions.Select(session => new SessionChoice(session)), SessionChoice.Back]));
            if (selectedChoice.Session is not SandboxSession selected) return;

            SessionAction action = AnsiConsole.Prompt(
                new SelectionPrompt<SessionAction>()
                    .UseConverter(SessionActionLabel)
                    .AddChoices(Enum.GetValues<SessionAction>()));
            switch (action)
            {
                case SessionAction.View:
                    AnsiConsole.Write(new Panel(new Text(_reports.ToConsole(selected))).Expand());
                    RenderArtifacts(selected);
                    break;
                case SessionAction.Json:
                    AnsiConsole.MarkupLine($"[green]{Markup.Escape(await WriteReportAsync(selected, "json", cancellationToken))}[/]");
                    break;
                case SessionAction.Html:
                    AnsiConsole.MarkupLine($"[green]{Markup.Escape(await WriteReportAsync(selected, "html", cancellationToken))}[/]");
                    break;
                case SessionAction.Delete:
                    if (AnsiConsole.Prompt(new ConfirmationPrompt(Markup.Escape(_text.Format("Sessions_DeleteConfirm", selected.Id))) { DefaultValue = false }))
                    {
                        string sessionsRoot = Path.Combine(_dataDirectory, "sessions");
                        if (!CleanupService.IsInside(sessionsRoot, selected.WorkspacePath))
                            throw new InvalidDataException("Session workspace is outside the SandForge data directory.");
                        if (Directory.Exists(selected.WorkspacePath)) Directory.Delete(selected.WorkspacePath, true);
                        await _store.DeleteAsync(selected.Id, cancellationToken);
                        AnsiConsole.MarkupLine($"[green]{Markup.Escape(_text.Format("Sessions_Deleted", selected.Id))}[/]");
                    }
                    break;
                case SessionAction.Back:
                    return;
            }
            Pause();
        }
    }

    private async Task ShowRecoveryAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.Write(new Rule($"[cyan]{Markup.Escape(_text["Recovery_Title"])}[/]"));
        RecoveryResult result = await _recovery.RecoverAsync(cancellationToken);
        AnsiConsole.MarkupLine(Markup.Escape(_text.Format(
            "Recovery_Result", result.Inspected, result.Recovered, result.Orphaned, result.Failed)));
    }

    private async Task ShowCleanupAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.Write(new Rule($"[cyan]{Markup.Escape(_text["Cleanup_Title"])}[/]"));
        string ageText = AnsiConsole.Prompt(new TextPrompt<string>("Older than:").DefaultValue("30d"));
        TimeSpan age = ParseAge(ageText);
        CleanupResult preview = await _cleanup.CleanupAsync(age, orphanedOnly: false, dryRun: true, cancellationToken);
        if (preview.Candidates.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(_text["Cleanup_None"])}[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("ID");
        table.AddColumn(_text["Report_Status"]);
        table.AddColumn(_text["Report_Size"]);
        table.AddColumn(_text["Report_Path"]);
        foreach (CleanupCandidate candidate in preview.Candidates)
            table.AddRow(Markup.Escape(candidate.SessionId), Markup.Escape(_text.Status(candidate.Status)), FormatBytes(candidate.SizeBytes), Markup.Escape(candidate.WorkspacePath));
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(Markup.Escape(_text.Format("Cleanup_Preview", preview.Candidates.Count, FormatBytes(preview.Candidates.Sum(x => x.SizeBytes)))));
        if (!AnsiConsole.Prompt(new ConfirmationPrompt(Markup.Escape(_text["Cleanup_Confirm"])) { DefaultValue = false })) return;
        CleanupResult result = await _cleanup.CleanupAsync(age, orphanedOnly: false, dryRun: false, cancellationToken);
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(_text.Format("Cleanup_Result", result.CleanedCount, FormatBytes(result.ReclaimedBytes)))}[/]");
    }

    private async Task ShowCacheAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            AnsiConsole.Write(new Rule($"[cyan]{Markup.Escape(_text["Cache_Title"])}[/]"));
            IReadOnlyList<CacheEntry> entries = await _cache.ListAsync(cancellationToken);
            if (entries.Count == 0) AnsiConsole.MarkupLine($"[grey]{Markup.Escape(_text["Cache_Empty"])}[/]");
            else
            {
                var table = new Table().Border(TableBorder.Rounded).Expand();
                table.AddColumn(_text["Report_Type"]);
                table.AddColumn(_text["Report_Size"]);
                table.AddColumn(_text["Report_Path"]);
                foreach (CacheEntry entry in entries)
                    table.AddRow(Markup.Escape(entry.Type), FormatBytes(entry.SizeBytes), Markup.Escape(entry.Path));
                AnsiConsole.Write(table);
            }

            CacheAction action = AnsiConsole.Prompt(
                new SelectionPrompt<CacheAction>()
                    .UseConverter(value => value == CacheAction.Clean ? _text["Cache_ActionClean"] : _text["Cache_ActionBack"])
                    .AddChoices(CacheAction.Clean, CacheAction.Back));
            if (action == CacheAction.Back) return;
            string[] types = entries.Select(x => x.Type).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
            string type = types.Length == 0 ? string.Empty : AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[cyan]{Markup.Escape(_text["Prompt_SelectCacheType"])}[/]")
                    .AddChoices(["*", .. types]));
            CacheCleanupResult result = await _cache.CleanupAsync(type == "*" || type.Length == 0 ? null : type, false, cancellationToken);
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(_text.Format("Cache_Result", result.RemovedEntries, FormatBytes(result.ReclaimedBytes)))}[/]");
            Pause();
        }
    }

    private async Task ShowUpdatesAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            AnsiConsole.Write(new Rule($"[cyan]{Markup.Escape(_text["Updates_Title"])}[/]"));
            UpdateSettings settings = await _updateSettingsStore.LoadAsync(cancellationToken);
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Setting");
            table.AddColumn("Value");
            table.AddRow("Repository", Markup.Escape(settings.Repository));
            table.AddRow("Channel", Markup.Escape(settings.Channel));
            table.AddRow("Auto check", settings.AutoCheck.ToString());
            table.AddRow("Auto apply", settings.AutoApply.ToString());
            table.AddRow("Interval", $"{settings.IntervalHours} h");
            AnsiConsole.Write(table);

            UpdateAction action = AnsiConsole.Prompt(
                new SelectionPrompt<UpdateAction>()
                    .UseConverter(UpdateActionLabel)
                    .AddChoices(UpdateAction.Check, UpdateAction.Install, UpdateAction.Back));
            if (action == UpdateAction.Back) return;
            UpdateCheckResult check = await _updater.CheckAsync("0.4.0-alpha", settings, cancellationToken);
            await _updateSettingsStore.RecordCheckAsync(cancellationToken);
            AnsiConsole.MarkupLine(Markup.Escape(check.Message));
            if (action == UpdateAction.Install && check.IsUpdateAvailable
                && AnsiConsole.Prompt(new ConfirmationPrompt(Markup.Escape(check.LatestVersion ?? _text["Updates_Install"])) { DefaultValue = false }))
            {
                UpdateApplyResult apply = await _updater.ApplyAsync(check, cancellationToken);
                AnsiConsole.MarkupLine(Markup.Escape(apply.Message));
            }
            Pause();
        }
    }

    private async Task OfferReportAsync(SandboxSession session, CancellationToken cancellationToken)
    {
        SessionAction action = AnsiConsole.Prompt(
            new SelectionPrompt<SessionAction>()
                .Title($"[cyan]{Markup.Escape(_text["Prompt_ReportFormat"])}[/]")
                .UseConverter(SessionActionLabel)
                .AddChoices(SessionAction.Html, SessionAction.Json, SessionAction.Back));
        if (action == SessionAction.Back) return;
        string format = action == SessionAction.Html ? "html" : "json";
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(await WriteReportAsync(session, format, cancellationToken))}[/]");
    }

    private async Task<string> WriteReportAsync(SandboxSession session, string format, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(_dataDirectory, "reports", session.Id);
        Directory.CreateDirectory(directory);
        return format == "html"
            ? await _reports.WriteHtmlAsync(session, Path.Combine(directory, "report.html"), cancellationToken)
            : await _reports.WriteJsonAsync(session, Path.Combine(directory, "report.json"), cancellationToken);
    }

    private void RenderArtifacts(SandboxSession session)
    {
        if (session.Artifacts.Count == 0) return;
        var table = new Table().Border(TableBorder.Simple).Expand();
        table.Title = new TableTitle(_text["Report_Artifacts"]);
        table.AddColumn(_text["Report_Type"]);
        table.AddColumn(_text["Report_Path"]);
        table.AddColumn(_text["Report_Size"]);
        foreach (SessionArtifact artifact in session.Artifacts.Take(30))
            table.AddRow(Markup.Escape(artifact.Type), Markup.Escape(artifact.RelativePath), FormatBytes(artifact.Size));
        AnsiConsole.Write(table);
    }

    private async Task<IReadOnlyList<TemplateChoice>> DiscoverTemplatesAsync(CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string root in TemplateRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (string path in Directory.EnumerateFiles(root, "sandforge.yaml", SearchOption.AllDirectories))
                paths.Add(Path.GetFullPath(path));
        }

        var choices = new List<TemplateChoice>();
        foreach (string path in paths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                TemplateDefinition template = await _templateEngine.LoadAsync(path, cancellationToken);
                choices.Add(new TemplateChoice(template.Metadata.Name, template.Metadata.DisplayName, path));
            }
            catch (InvalidDataException) { }
        }
        return choices.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private IEnumerable<string> TemplateRoots()
    {
        yield return Path.Combine(_baseDirectory, "templates");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "templates");
        yield return Path.GetFullPath(Path.Combine(_baseDirectory, "..", "..", "..", "..", "..", "templates"));
    }

    private string ProgressText(SessionProgress progress) => progress.Status switch
    {
        SessionStatus.Validating => _text["Run_Validating"],
        SessionStatus.Preparing or SessionStatus.Ready => _text["Run_Preparing"],
        SessionStatus.Starting => _text["Run_Launching"],
        SessionStatus.Running => progress.TotalCollectors > 0
            ? $"{_text["Run_Running"]} ({progress.TotalCollectors} collectors)"
            : _text["Run_Running"],
        SessionStatus.Collecting => _text["Run_Collecting"],
        _ => _text.Status(progress.Status)
    };

    private string ActionLabel(TuiAction action) => action switch
    {
        TuiAction.Run => _text["Menu_Run"],
        TuiAction.TestInstaller => _text["Menu_TestInstaller"],
        TuiAction.Sessions => _text["Menu_Sessions"],
        TuiAction.Recovery => _text["Menu_Recovery"],
        TuiAction.Cleanup => _text["Menu_Cleanup"],
        TuiAction.Cache => _text["Menu_Cache"],
        TuiAction.Updates => _text["Menu_Updates"],
        _ => _text["Menu_Exit"]
    };

    private string SessionActionLabel(SessionAction action) => action switch
    {
        SessionAction.View => _text["Sessions_ActionView"],
        SessionAction.Json => _text["Sessions_ActionJson"],
        SessionAction.Html => _text["Sessions_ActionHtml"],
        SessionAction.Delete => _text["Sessions_ActionDelete"],
        _ => _text["Sessions_ActionBack"]
    };

    private string UpdateActionLabel(UpdateAction action) => action switch
    {
        UpdateAction.Check => _text["Updates_Check"],
        UpdateAction.Install => _text["Updates_Install"],
        _ => _text["Updates_Back"]
    };

    private string RiskMarkup(RiskLevel risk)
    {
        string color = risk switch
        {
            RiskLevel.Critical => "bold red",
            RiskLevel.High => "red",
            RiskLevel.Medium => "yellow",
            _ => "green"
        };
        return $"[{color}]{Markup.Escape(_text.Risk(risk))}[/]";
    }

    private string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double number = value;
        int index = 0;
        while (number >= 1024 && index < units.Length - 1) { number /= 1024; index++; }
        return $"{number.ToString("0.##", _text.Culture)} {units[index]}";
    }

    private static TimeSpan ParseAge(string text)
    {
        text = text.Trim().ToLowerInvariant();
        if (text.EndsWith('d') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double days)) return TimeSpan.FromDays(days);
        if (text.EndsWith('h') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double hours)) return TimeSpan.FromHours(hours);
        throw new InvalidDataException("Age must use a value such as 30d or 12h.");
    }

    private void Pause()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Markup($"[grey]{Markup.Escape(_text["Prompt_Continue"])}[/]");
        Console.ReadLine();
    }

    private enum TuiAction { Run, TestInstaller, Sessions, Recovery, Cleanup, Cache, Updates, Exit }
    private enum SessionAction { View, Json, Html, Delete, Back }
    private enum CacheAction { Clean, Back }
    private enum UpdateAction { Check, Install, Back }
    private sealed record TemplateChoice(string Name, string DisplayName, string Path);
    private sealed record SessionChoice(SandboxSession? Session)
    {
        public static SessionChoice Back { get; } = new(Session: null);
    }

    private sealed class DelegateProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
