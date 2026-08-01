using System.Security;
using System.Text;
using System.Text.Json;
using SandForge.Core;
using SandForge.Domain;

namespace SandForge.Sandbox;

public sealed class SandboxConfigurationGenerator : ISandboxConfigurationGenerator
{
    public async Task<string> GenerateAsync(
        SessionPlan plan,
        SessionWorkspace workspace,
        CancellationToken cancellationToken)
    {
        string guestInput = @"C:\Sandbox\Input";
        string guestOutput = @"C:\Sandbox\Output";
        string guestBootstrap = @"C:\Sandbox\Bootstrap";
        string copiedTarget = Path.Combine(workspace.Input, plan.TargetFileName);
        if (!File.Exists(copiedTarget)) throw new FileNotFoundException("Copied target is missing.", copiedTarget);

        string bootstrapPath = Path.Combine(workspace.Bootstrap, "bootstrap.ps1");
        string targetGuestPath = string.IsNullOrWhiteSpace(plan.Target.Executable)
            ? $@"{guestInput}\{plan.TargetFileName}"
            : plan.Target.Executable.Replace("${targetFileName}", plan.TargetFileName, StringComparison.Ordinal);
        string script = BuildBootstrap(plan, targetGuestPath, guestOutput);
        await File.WriteAllTextAsync(bootstrapPath, script, new UTF8Encoding(false), cancellationToken);

        string configPath = Path.Combine(workspace.Config, $"{plan.SessionId}.wsb");
        string xml = BuildWsb(plan, workspace, guestInput, guestOutput, guestBootstrap);
        await File.WriteAllTextAsync(configPath, xml, new UTF8Encoding(false), cancellationToken);
        return configPath;
    }

    private static string BuildWsb(SessionPlan plan, SessionWorkspace workspace, string guestInput, string guestOutput, string guestBootstrap)
    {
        string networking = plan.Sandbox.Network == NetworkPolicy.Disabled ? "Disable" : "Default";
        string clipboard = plan.Sandbox.Clipboard == ClipboardPolicy.Disabled ? "Disable" : "Default";
        string protectedClient = plan.Sandbox.ProtectedClient ? "Enable" : "Disable";
        string command = $@"powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File {guestBootstrap}\bootstrap.ps1";

        var xml = new StringBuilder();
        xml.AppendLine("<Configuration>");
        xml.AppendLine($"  <Networking>{networking}</Networking>");
        xml.AppendLine($"  <ClipboardRedirection>{clipboard}</ClipboardRedirection>");
        xml.AppendLine("  <PrinterRedirection>Disable</PrinterRedirection>");
        xml.AppendLine("  <AudioInput>Disable</AudioInput>");
        xml.AppendLine("  <VideoInput>Disable</VideoInput>");
        xml.AppendLine($"  <ProtectedClient>{protectedClient}</ProtectedClient>");
        xml.AppendLine($"  <MemoryInMB>{plan.Sandbox.MemoryMb}</MemoryInMB>");
        xml.AppendLine("  <MappedFolders>");
        AppendFolder(xml, workspace.Input, guestInput, true);
        AppendFolder(xml, workspace.Output, guestOutput, false);
        AppendFolder(xml, workspace.Bootstrap, guestBootstrap, true);
        foreach (SessionMount mount in plan.Mounts.Where(x => x.Mode is MountMode.ReadOnly or MountMode.ReadWrite))
            AppendFolder(xml, mount.HostPath, mount.GuestPath, mount.Mode == MountMode.ReadOnly);
        xml.AppendLine("  </MappedFolders>");
        xml.AppendLine("  <LogonCommand>");
        xml.AppendLine($"    <Command>{SecurityElement.Escape(command)}</Command>");
        xml.AppendLine("  </LogonCommand>");
        xml.AppendLine("</Configuration>");
        return xml.ToString();
    }

    private static void AppendFolder(StringBuilder xml, string host, string guest, bool readOnly)
    {
        xml.AppendLine("    <MappedFolder>");
        xml.AppendLine($"      <HostFolder>{SecurityElement.Escape(Path.GetFullPath(host))}</HostFolder>");
        xml.AppendLine($"      <SandboxFolder>{SecurityElement.Escape(guest)}</SandboxFolder>");
        xml.AppendLine($"      <ReadOnly>{readOnly.ToString().ToLowerInvariant()}</ReadOnly>");
        xml.AppendLine("    </MappedFolder>");
    }

    private static string BuildBootstrap(SessionPlan plan, string targetGuestPath, string guestOutput)
    {
        string argsJson = JsonSerializer.Serialize(plan.Target.Arguments.Select(x => x.Replace("${targetFileName}", plan.TargetFileName, StringComparison.Ordinal)));
        string escapedTarget = targetGuestPath.Replace("'", "''", StringComparison.Ordinal);
        string escapedWorking = plan.Target.WorkingDirectory.Replace("'", "''", StringComparison.Ordinal);
        string escapedSession = plan.SessionId.Replace("'", "''", StringComparison.Ordinal);
        string escapedOutput = guestOutput.Replace("'", "''", StringComparison.Ordinal);
        return $$"""
        $ErrorActionPreference = 'Stop'
        $sessionId = '{{escapedSession}}'
        $outputRoot = '{{escapedOutput}}'
        $metaRoot = Join-Path $outputRoot '.sandforge'
        New-Item -ItemType Directory -Force -Path $outputRoot, $metaRoot | Out-Null

        $arguments = ConvertFrom-Json @'
        {{argsJson}}
        '@

        $startedAt = [DateTimeOffset]::UtcNow
        $exitCode = 1
        try {
            Push-Location '{{escapedWorking}}'
            & '{{escapedTarget}}' @arguments
            $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { [int]$LASTEXITCODE }
        }
        catch {
            $_ | Out-String | Set-Content -Encoding UTF8 (Join-Path $metaRoot 'bootstrap-error.txt')
            $exitCode = 1
        }
        finally {
            Pop-Location -ErrorAction SilentlyContinue
            $marker = [ordered]@{
                schemaVersion = 1
                sessionId = $sessionId
                targetExitCode = $exitCode
                startedAt = $startedAt.ToString('O')
                endedAt = [DateTimeOffset]::UtcNow.ToString('O')
            }
            $marker | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $metaRoot 'completed.json')
            Start-Sleep -Seconds 1
            Start-Process shutdown.exe -ArgumentList '/s','/t','1' -WindowStyle Hidden
        }
        """;
    }
}
