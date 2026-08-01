using System.Diagnostics;
using SandForge.Core;
using SandForge.Domain;

namespace SandForge.Sandbox;

public sealed class WindowsSandboxBackend : ISandboxBackend
{
    public async Task<SandboxAvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(false, "SandForge requires Windows 10 or Windows 11.");
        if (!Environment.Is64BitOperatingSystem) return new(false, "A 64-bit Windows installation is required.");

        string windowsSandbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsSandbox.exe");
        if (File.Exists(windowsSandbox)) return new(true, "Windows Sandbox executable is available.");

        string dism = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "dism.exe");
        if (!File.Exists(dism)) return new(false, "DISM was not found; Windows Sandbox availability could not be checked.");

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
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start DISM.");
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        bool enabled = process.ExitCode == 0 && output.Contains("Enabled", StringComparison.OrdinalIgnoreCase);
        return enabled
            ? new(true, "Windows Sandbox feature is enabled.")
            : new(false, "Windows Sandbox feature is disabled or unsupported. Run: sandforge feature enable");
    }

    public Task<SandboxLaunchResult> LaunchAsync(string configurationPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(configurationPath)) return Task.FromResult(new SandboxLaunchResult(false, null, "Sandbox configuration file was not found."));
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
                ? new SandboxLaunchResult(false, null, "Windows did not start the .wsb configuration.")
                : new SandboxLaunchResult(true, process.Id, "Windows Sandbox started."));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Task.FromResult(new SandboxLaunchResult(false, null, exception.Message));
        }
    }
}
