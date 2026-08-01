using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SandForge.Domain;

namespace SandForge.Core;

public sealed record UpdateSettings
{
    public bool AutoCheck { get; init; } = true;
    public bool AutoApply { get; init; }
    public string Channel { get; init; } = "preview";
    public int IntervalHours { get; init; } = 24;
    public string Repository { get; init; } = "Onmaynec/SandForge";
}

public sealed class UpdateSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _settingsPath;
    private readonly string _statePath;

    public UpdateSettingsStore(string dataDirectory)
    {
        string root = Path.Combine(Path.GetFullPath(dataDirectory), "updates");
        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "settings.json");
        _statePath = Path.Combine(root, "last-check.txt");
    }

    public async Task<UpdateSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath)) return new UpdateSettings();
        await using FileStream stream = File.OpenRead(_settingsPath);
        UpdateSettings settings = await JsonSerializer.DeserializeAsync<UpdateSettings>(stream, JsonOptions, cancellationToken) ?? new UpdateSettings();
        Validate(settings);
        return settings;
    }

    public async Task SaveAsync(UpdateSettings settings, CancellationToken cancellationToken)
    {
        Validate(settings);
        await File.WriteAllTextAsync(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false), cancellationToken);
    }

    public bool IsCheckDue(UpdateSettings settings)
    {
        if (!settings.AutoCheck) return false;
        if (!File.Exists(_statePath)) return true;
        string text = File.ReadAllText(_statePath).Trim();
        return !DateTimeOffset.TryParse(text, out DateTimeOffset last) || DateTimeOffset.UtcNow - last >= TimeSpan.FromHours(settings.IntervalHours);
    }

    public Task RecordCheckAsync(CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(_statePath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);

    private static void Validate(UpdateSettings settings)
    {
        if (settings.Channel is not "stable" and not "preview") throw new InvalidDataException("Канал обновлений должен быть stable или preview.");
        if (settings.IntervalHours is < 1 or > 720) throw new InvalidDataException("Интервал проверки обновлений должен быть от 1 до 720 часов.");
        if (settings.Repository.Split('/').Length != 2 || settings.Repository.Any(char.IsWhiteSpace)) throw new InvalidDataException("Repository обновлений должен иметь формат owner/name.");
    }
}

public sealed class UpdateService
{
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly string _baseDirectory;
    private readonly string _updatesDirectory;
    private readonly HttpClient _httpClient;

    public UpdateService(string baseDirectory, string dataDirectory, HttpClient? httpClient = null)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _updatesDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "updates");
        Directory.CreateDirectory(_updatesDirectory);
        _httpClient = httpClient ?? SharedClient;
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, UpdateSettings settings, CancellationToken cancellationToken)
    {
        string endpoint = $"https://api.github.com/repos/{settings.Repository}/releases?per_page=30";
        using HttpResponseMessage response = await _httpClient.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new UpdateCheckResult(currentVersion, null, false, null, null, $"GitHub вернул HTTP {(int)response.StatusCode}.");

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        List<ReleaseDto> releases = await JsonSerializer.DeserializeAsync<List<ReleaseDto>>(stream, JsonOptions, cancellationToken) ?? [];
        ReleaseDto? release = releases
            .Where(x => !x.Draft && (settings.Channel == "preview" || !x.Prerelease))
            .Select(x => new { Release = x, Version = SemanticVersion.TryParse(x.TagName) })
            .Where(x => x.Version is not null)
            .OrderByDescending(x => x.Version)
            .Select(x => x.Release)
            .FirstOrDefault();
        if (release is null) return new UpdateCheckResult(currentVersion, null, false, null, null, "Подходящие GitHub Releases не найдены.");

        string latest = NormalizeVersion(release.TagName);
        string packageName = $"SandForge-{latest}-win-x64.zip";
        AssetDto? package = release.Assets.FirstOrDefault(x => x.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
        AssetDto? checksum = release.Assets.FirstOrDefault(x => x.Name.Equals(packageName + ".sha256", StringComparison.OrdinalIgnoreCase));
        bool available = SemanticVersion.Compare(latest, currentVersion) > 0;
        string message = available
            ? $"Доступна версия {latest}."
            : $"Установлена актуальная версия {currentVersion}.";
        if (available && (package is null || checksum is null))
            return new UpdateCheckResult(currentVersion, latest, false, null, null, $"Релиз {latest} не содержит package или SHA-256.");
        return new UpdateCheckResult(currentVersion, latest, available, package?.BrowserDownloadUrl, checksum?.BrowserDownloadUrl, message);
    }

    public async Task<UpdateApplyResult> ApplyAsync(UpdateCheckResult check, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new UpdateApplyResult(false, "Автообновление package поддерживается только на Windows.", null);
        if (!check.IsUpdateAvailable || string.IsNullOrWhiteSpace(check.LatestVersion) || string.IsNullOrWhiteSpace(check.PackageUrl) || string.IsNullOrWhiteSpace(check.ChecksumUrl))
            return new UpdateApplyResult(false, "Нет подготовленного обновления.", null);
        string executable = Path.Combine(_baseDirectory, "sandforge.exe");
        if (!File.Exists(executable)) return new UpdateApplyResult(false, "Команда update install доступна только в опубликованной win-x64 сборке.", null);

        ValidateDownloadUrl(check.PackageUrl);
        ValidateDownloadUrl(check.ChecksumUrl);
        string versionRoot = Path.Combine(_updatesDirectory, check.LatestVersion);
        string packagePath = Path.Combine(versionRoot, "package.zip");
        string stagePath = Path.Combine(versionRoot, "stage");
        string backupPath = Path.Combine(versionRoot, "backup");
        Directory.CreateDirectory(versionRoot);
        if (Directory.Exists(stagePath)) Directory.Delete(stagePath, true);
        Directory.CreateDirectory(stagePath);

        await DownloadAsync(check.PackageUrl, packagePath, cancellationToken);
        string checksumText = await _httpClient.GetStringAsync(check.ChecksumUrl, cancellationToken);
        string expectedHash = ParseChecksum(checksumText);
        string actualHash = await ComputeSha256Async(packagePath, cancellationToken);
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA-256 обновления не совпадает. Ожидался {expectedHash}, получен {actualHash}.");

        ExtractSafely(packagePath, stagePath);
        if (!File.Exists(Path.Combine(stagePath, "sandforge.exe"))) throw new InvalidDataException("Package обновления не содержит sandforge.exe.");
        string scriptPath = Path.Combine(versionRoot, "apply-update.ps1");
        await File.WriteAllTextAsync(scriptPath, BuildApplyScript(), new UTF8Encoding(false), cancellationToken);

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        start.ArgumentList.Add("-ProcessId");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("-InstallPath");
        start.ArgumentList.Add(_baseDirectory);
        start.ArgumentList.Add("-StagePath");
        start.ArgumentList.Add(stagePath);
        start.ArgumentList.Add("-BackupPath");
        start.ArgumentList.Add(backupPath);
        start.ArgumentList.Add("-Version");
        start.ArgumentList.Add(check.LatestVersion);
        Process.Start(start);
        return new UpdateApplyResult(true, $"Обновление до {check.LatestVersion} подготовлено и будет применено после завершения SandForge.", scriptPath);
    }

    private async Task DownloadAsync(string url, string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static void ExtractSafely(string packagePath, string destination)
    {
        string normalizedDestination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(normalizedDestination, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package обновления содержит небезопасный путь.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private static string BuildApplyScript() => """
        param(
          [Parameter(Mandatory=$true)][int]$ProcessId,
          [Parameter(Mandatory=$true)][string]$InstallPath,
          [Parameter(Mandatory=$true)][string]$StagePath,
          [Parameter(Mandatory=$true)][string]$BackupPath,
          [Parameter(Mandatory=$true)][string]$Version
        )
        $ErrorActionPreference='Stop'
        Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
        Remove-Item $BackupPath -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path $BackupPath | Out-Null
        $preservePortable = Test-Path (Join-Path $InstallPath 'portable.mode')
        try {
          Get-ChildItem $InstallPath -Force | Where-Object { $_.Name -notin @('data') } | ForEach-Object {
            Copy-Item $_.FullName $BackupPath -Recurse -Force
          }
          Get-ChildItem $StagePath -Force | Where-Object { $_.Name -notin @('portable.mode','data') } | ForEach-Object {
            Copy-Item $_.FullName $InstallPath -Recurse -Force
          }
          if($preservePortable){ New-Item -ItemType File -Force -Path (Join-Path $InstallPath 'portable.mode') | Out-Null }
          $verify=Start-Process -FilePath (Join-Path $InstallPath 'sandforge.exe') -ArgumentList '--version' -Wait -PassThru
          if($verify.ExitCode -ne 0){ throw "Новая версия завершила self-check с кодом $($verify.ExitCode)." }
          "SandForge $Version installed at $([DateTimeOffset]::UtcNow.ToString('O'))" | Set-Content -Encoding UTF8 (Join-Path $InstallPath 'update.log')
        } catch {
          Get-ChildItem $BackupPath -Force | ForEach-Object { Copy-Item $_.FullName $InstallPath -Recurse -Force }
          $_ | Out-String | Set-Content -Encoding UTF8 (Join-Path $InstallPath 'update-error.log')
          exit 1
        }
        """;

    private static string ParseChecksum(string text)
    {
        string value = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (value.Length != 64 || value.Any(x => !Uri.IsHexDigit(x))) throw new InvalidDataException("Файл SHA-256 релиза имеет неверный формат.");
        return value.ToUpperInvariant();
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void ValidateDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("URL обновления должен использовать HTTPS.");
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("URL обновления ведёт на недоверенный host.");
    }

    private static string NormalizeVersion(string value) => value.Trim().TrimStart('v', 'V');

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SandForge", "0.3"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record ReleaseDto(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] IReadOnlyList<AssetDto> Assets);

    private sealed record AssetDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

    private sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        private SemanticVersion(int major, int minor, int patch, string? prerelease)
        {
            Major = major; Minor = minor; Patch = patch; Prerelease = prerelease;
        }
        private int Major { get; }
        private int Minor { get; }
        private int Patch { get; }
        private string? Prerelease { get; }

        public static SemanticVersion? TryParse(string value)
        {
            string normalized = NormalizeVersion(value);
            string[] parts = normalized.Split('-', 2);
            string[] numbers = parts[0].Split('.');
            if (numbers.Length < 2 || !int.TryParse(numbers[0], out int major) || !int.TryParse(numbers[1], out int minor)) return null;
            int patch = numbers.Length > 2 && int.TryParse(numbers[2], out int parsedPatch) ? parsedPatch : 0;
            return new SemanticVersion(major, minor, patch, parts.Length > 1 ? parts[1] : null);
        }

        public static int Compare(string left, string right)
        {
            SemanticVersion? a = TryParse(left);
            SemanticVersion? b = TryParse(right);
            if (a is null || b is null) return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            return a.CompareTo(b);
        }

        public int CompareTo(SemanticVersion? other)
        {
            if (other is null) return 1;
            int core = Major.CompareTo(other.Major);
            if (core == 0) core = Minor.CompareTo(other.Minor);
            if (core == 0) core = Patch.CompareTo(other.Patch);
            if (core != 0) return core;
            if (Prerelease is null && other.Prerelease is not null) return 1;
            if (Prerelease is not null && other.Prerelease is null) return -1;
            return string.Compare(Prerelease, other.Prerelease, StringComparison.OrdinalIgnoreCase);
        }
    }
}
