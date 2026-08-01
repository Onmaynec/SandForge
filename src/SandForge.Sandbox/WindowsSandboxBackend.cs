using System.Diagnostics;
using SandForge.Core;
using SandForge.Domain;

namespace SandForge.Sandbox;

public sealed class WindowsSandboxBackend : ISandboxBackend
{
    public async Task<SandboxAvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(false, "SandForge требуется Windows 10 или Windows 11.");
        if (!Environment.Is64BitOperatingSystem) return new(false, "Требуется 64-разрядная Windows.");

        string windowsSandbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsSandbox.exe");
        if (File.Exists(windowsSandbox)) return new(true, "Windows Sandbox доступна.");

        string dism = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "dism.exe");
        if (!File.Exists(dism)) return new(false, "DISM не найден; доступность Windows Sandbox проверить не удалось.");

        var startInfo = new ProcessStartInfo(dism)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/Online");
        startInfo.ArgumentList.Add("/Get-FeatureInfo");
        startInfo.ArgumentList.Add("/FeatureName:Containers-DisposableClientVM");
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить DISM.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string output = await outputTask;
        _ = await errorTask;
        bool enabled = process.ExitCode == 0 && output.Contains("Enabled", StringComparison.OrdinalIgnoreCase);
        return enabled
            ? new(true, "Компонент Windows Sandbox включён.")
            : new(false, "Компонент Windows Sandbox отключён или не поддерживается.");
    }

    public Task<SandboxLaunchResult> LaunchAsync(string configurationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(configurationPath)) return Task.FromResult(new SandboxLaunchResult(false, null, "Файл конфигурации Sandbox не найден."));
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = configurationPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(configurationPath)!
            };
            Process? process = Process.Start(startInfo);
            return Task.FromResult(process is null
                ? new SandboxLaunchResult(false, null, "Windows не запустила конфигурацию .wsb.")
                : new SandboxLaunchResult(true, process.Id, "Windows Sandbox запущена."));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Task.FromResult(new SandboxLaunchResult(false, null, exception.Message));
        }
    }
}
