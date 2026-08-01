using System.Collections;
using System.Globalization;
using System.Resources;
using SandForge.Domain;

namespace SandForge.Reporting;

/// <summary>
/// Shared localized text for the CLI, TUI and generated reports.
/// Russian is the neutral fallback; English is selected explicitly or through auto detection.
/// </summary>
public sealed class UiText
{
    private static readonly ResourceManager ResourceManager =
        new("SandForge.Reporting.Resources.Strings", typeof(UiText).Assembly);

    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    private UiText(CultureInfo culture)
    {
        Culture = culture;
        LanguageCode = culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
    }

    public CultureInfo Culture { get; }
    public string LanguageCode { get; }
    public string HtmlLanguage => LanguageCode;

    public static UiText Russian { get; } = new(RussianCulture);
    public static UiText English { get; } = new(EnglishCulture);

    public static UiText FromSetting(string? setting)
    {
        string normalized = setting?.Trim().ToLowerInvariant() ?? "ru";
        if (normalized == "en") return English;
        if (normalized == "auto")
        {
            string current = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return current.Equals("en", StringComparison.OrdinalIgnoreCase) ? English : Russian;
        }
        return Russian;
    }

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return ResourceManager.GetString(key, Culture)
            ?? ResourceManager.GetString(key, RussianCulture)
            ?? throw new MissingManifestResourceException($"Missing localization key: {key}");
    }

    public string Format(string key, params object?[] arguments) =>
        string.Format(Culture, Get(key), arguments);

    public string Status(SessionStatus value) => Get($"Status_{value}");
    public string Risk(RiskLevel value) => Get($"Risk_{value}");
    public string Cleanup(CleanupState value) => Get($"Cleanup_{value}");
    public string Network(NetworkPolicy value) => Get($"Network_{value}");
    public string Clipboard(ClipboardPolicy value) => Get($"Clipboard_{value}");

    public static IReadOnlyList<string> MissingKeys(string language)
    {
        CultureInfo culture = language.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("en")
            : CultureInfo.InvariantCulture;
        ResourceSet? resourceSet = ResourceManager.GetResourceSet(culture, true, false);
        if (resourceSet is null) return RequiredKeys;

        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in resourceSet)
            if (entry.Key is string key) available.Add(key);
        return RequiredKeys.Where(key => !available.Contains(key)).ToArray();
    }

    public static IReadOnlyList<string> RequiredKeys { get; } =
    [
        "App_Title", "App_Subtitle", "App_Version",
        "Help_Text", "Error_UnknownCommand", "Error_OperationCancelled", "Error_Generic",
        "Dashboard_Title", "Dashboard_Environment", "Dashboard_RecentSessions", "Dashboard_NoSessions",
        "Menu_Run", "Menu_TestInstaller", "Menu_Sessions", "Menu_Recovery", "Menu_Cleanup",
        "Menu_Cache", "Menu_Updates", "Menu_Exit",
        "Prompt_TargetPath", "Prompt_Template", "Prompt_ConfirmLaunch", "Prompt_ConfirmDangerousLaunch",
        "Prompt_Continue", "Prompt_ReportFormat", "Prompt_SelectSession", "Prompt_SelectCacheType",
        "Security_Title", "Security_Risk", "Security_Network", "Security_Clipboard", "Security_Timeout",
        "Security_Mounts", "Security_Collectors", "Security_Findings", "Security_NoFindings", "Security_Blocked",
        "Run_Preparing", "Run_Validating", "Run_Launching", "Run_Running", "Run_Collecting", "Run_Completed",
        "Sessions_Title", "Sessions_Empty", "Sessions_ActionView", "Sessions_ActionJson", "Sessions_ActionHtml",
        "Sessions_ActionDelete", "Sessions_ActionBack", "Sessions_DeleteConfirm", "Sessions_Deleted",
        "Recovery_Title", "Recovery_Result", "Cleanup_Title", "Cleanup_None", "Cleanup_Preview", "Cleanup_Confirm",
        "Cleanup_Result", "Cache_Title", "Cache_Empty", "Cache_ActionClean", "Cache_ActionBack", "Cache_Result",
        "Updates_Title", "Updates_Status", "Updates_Check", "Updates_Install", "Updates_Back",
        "Report_Title", "Report_Offline", "Report_Session", "Report_Template", "Report_Status", "Report_Risk",
        "Report_TargetHash", "Report_Artifacts", "Report_Collectors", "Report_Cleanup", "Report_Error",
        "Report_Collector", "Report_Changes", "Report_File", "Report_State", "Report_Type", "Report_Path",
        "Report_Size", "Report_Ok", "Report_CollectorError",
        "Status_Created", "Status_Validating", "Status_Preparing", "Status_Ready", "Status_Starting",
        "Status_Running", "Status_Stopping", "Status_Collecting", "Status_Completed", "Status_Partial",
        "Status_Failed", "Status_Cancelled", "Status_TimedOut", "Status_Orphaned",
        "Risk_Low", "Risk_Medium", "Risk_High", "Risk_Critical",
        "Cleanup_Pending", "Cleanup_Kept", "Cleanup_Cleaned",
        "Network_Disabled", "Network_Enabled", "Network_Required",
        "Clipboard_Disabled", "Clipboard_Enabled"
    ];
}
