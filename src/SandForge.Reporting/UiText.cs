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

    private static readonly IReadOnlyDictionary<string, string> RussianFallback = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Schema_Title"] = "КОНТРАКТЫ И СХЕМЫ SANDFORGE",
        ["Schema_Id"] = "Контракт",
        ["Schema_Current"] = "Текущая",
        ["Schema_Supported"] = "Поддержка",
        ["Schema_Deprecated"] = "Устарели",
        ["Schema_Syntax"] = "Синтаксис",
        ["Schema_File"] = "Файл схемы",
        ["Schema_Detected"] = "Контракт: {0}",
        ["Schema_Version"] = "Версия схемы: {0}",
        ["Schema_Valid"] = "Документ совместим.",
        ["Schema_Invalid"] = "Документ несовместим.",
        ["Schema_Warning"] = "Предупреждение: {0}",
        ["Schema_Error"] = "Ошибка: {0}",
        ["Schema_Usage"] = "Использование: sandforge schema list | describe <id> | validate <файл> [--contract <id>]",
        ["Schema_UnknownContract"] = "Неизвестный контракт: {0}."
    };

    private static readonly IReadOnlyDictionary<string, string> EnglishFallback = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Schema_Title"] = "SANDFORGE CONTRACTS AND SCHEMAS",
        ["Schema_Id"] = "Contract",
        ["Schema_Current"] = "Current",
        ["Schema_Supported"] = "Supported",
        ["Schema_Deprecated"] = "Deprecated",
        ["Schema_Syntax"] = "Syntax",
        ["Schema_File"] = "Schema file",
        ["Schema_Detected"] = "Contract: {0}",
        ["Schema_Version"] = "Schema version: {0}",
        ["Schema_Valid"] = "The document is compatible.",
        ["Schema_Invalid"] = "The document is incompatible.",
        ["Schema_Warning"] = "Warning: {0}",
        ["Schema_Error"] = "Error: {0}",
        ["Schema_Usage"] = "Usage: sandforge schema list | describe <id> | validate <file> [--contract <id>]",
        ["Schema_UnknownContract"] = "Unknown contract: {0}."
    };

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
        string? resource = ResourceManager.GetString(key, Culture)
            ?? ResourceManager.GetString(key, RussianCulture);
        if (resource is not null) return resource;

        IReadOnlyDictionary<string, string> fallback = LanguageCode == "en" ? EnglishFallback : RussianFallback;
        if (fallback.TryGetValue(key, out string? value)) return value;
        if (RussianFallback.TryGetValue(key, out value)) return value;
        throw new MissingManifestResourceException($"Missing localization key: {key}");
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
        bool english = language.Equals("en", StringComparison.OrdinalIgnoreCase);
        CultureInfo culture = english ? CultureInfo.GetCultureInfo("en") : CultureInfo.InvariantCulture;
        ResourceSet? resourceSet = ResourceManager.GetResourceSet(culture, true, false);
        var available = new HashSet<string>(StringComparer.Ordinal);
        if (resourceSet is not null)
        {
            foreach (DictionaryEntry entry in resourceSet)
                if (entry.Key is string key) available.Add(key);
        }

        foreach (string key in (english ? EnglishFallback : RussianFallback).Keys)
            available.Add(key);
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
        "Schema_Title", "Schema_Id", "Schema_Current", "Schema_Supported", "Schema_Deprecated", "Schema_Syntax",
        "Schema_File", "Schema_Detected", "Schema_Version", "Schema_Valid", "Schema_Invalid", "Schema_Warning",
        "Schema_Error", "Schema_Usage", "Schema_UnknownContract",
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
