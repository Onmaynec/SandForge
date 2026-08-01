using System.Security;
using System.Text;
using System.Text.Json;
using SandForge.Core;
using SandForge.Domain;

namespace SandForge.Sandbox;

public sealed class SandboxConfigurationGenerator : ISandboxConfigurationGenerator
{
    public async Task<string> GenerateAsync(SessionPlan plan, SessionWorkspace workspace, CancellationToken cancellationToken)
    {
        const string guestInput = @"C:\Sandbox\Input";
        const string guestOutput = @"C:\Sandbox\Output";
        const string guestBootstrap = @"C:\Sandbox\Bootstrap";
        string copiedTarget = Path.Combine(workspace.Input, plan.TargetFileName);
        if (!File.Exists(copiedTarget)) throw new FileNotFoundException("Скопированный целевой файл отсутствует.", copiedTarget);
        string targetGuestPath = string.IsNullOrWhiteSpace(plan.Target.Executable)
            ? $@"{guestInput}\{plan.TargetFileName}"
            : plan.Target.Executable.Replace("${targetFileName}", plan.TargetFileName, StringComparison.Ordinal);
        string bootstrapPath = Path.Combine(workspace.Bootstrap, "bootstrap.ps1");
        await File.WriteAllTextAsync(bootstrapPath, BuildBootstrap(plan, targetGuestPath, guestOutput), new UTF8Encoding(false), cancellationToken);
        string configPath = Path.Combine(workspace.Config, $"{plan.SessionId}.wsb");
        await File.WriteAllTextAsync(configPath, BuildWsb(plan, workspace, guestInput, guestOutput, guestBootstrap), new UTF8Encoding(false), cancellationToken);
        return configPath;
    }

    private static string BuildWsb(SessionPlan plan, SessionWorkspace workspace, string guestInput, string guestOutput, string guestBootstrap)
    {
        string command = $@"powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File {guestBootstrap}\bootstrap.ps1";
        var xml = new StringBuilder();
        xml.AppendLine("<Configuration>");
        xml.AppendLine($"  <Networking>{(plan.Sandbox.Network == NetworkPolicy.Disabled ? "Disable" : "Default")}</Networking>");
        xml.AppendLine($"  <ClipboardRedirection>{(plan.Sandbox.Clipboard == ClipboardPolicy.Disabled ? "Disable" : "Default")}</ClipboardRedirection>");
        xml.AppendLine("  <PrinterRedirection>Disable</PrinterRedirection>");
        xml.AppendLine("  <AudioInput>Disable</AudioInput>");
        xml.AppendLine("  <VideoInput>Disable</VideoInput>");
        xml.AppendLine($"  <ProtectedClient>{(plan.Sandbox.ProtectedClient ? "Enable" : "Disable")}</ProtectedClient>");
        xml.AppendLine($"  <MemoryInMB>{plan.Sandbox.MemoryMb}</MemoryInMB>");
        xml.AppendLine("  <MappedFolders>");
        AppendFolder(xml, workspace.Input, guestInput, true);
        AppendFolder(xml, workspace.Output, guestOutput, false);
        AppendFolder(xml, workspace.Bootstrap, guestBootstrap, true);
        foreach (SessionMount mount in plan.Mounts.Concat(plan.CacheMounts).Where(x => x.Mode is MountMode.ReadOnly or MountMode.ReadWrite))
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
        string collectorsJson = JsonSerializer.Serialize(plan.ArtifactCollectors.Select(x => x.ToLowerInvariant()));
        string packagesJson = JsonSerializer.Serialize(plan.Packages.Select(x => new { id = x.Id, version = x.Version, source = x.Source }));
        string installersJson = JsonSerializer.Serialize(plan.Installers.Select(x => new
        {
            path = x.GuestPath,
            sha256 = x.Sha256,
            arguments = x.Arguments,
            timeoutSeconds = Math.Max(1, (int)x.Timeout.TotalSeconds)
        }));
        string cacheTypesJson = JsonSerializer.Serialize(plan.CacheMounts.Select(x => x.GuestPath.Split('\\').Last().ToLowerInvariant()));
        string failurePolicy = plan.ProvisioningFailurePolicy.ToString().ToLowerInvariant();
        string escapedTarget = Ps(targetGuestPath);
        string escapedWorking = Ps(plan.Target.WorkingDirectory);
        string escapedSession = Ps(plan.SessionId);
        string escapedOutput = Ps(guestOutput);
        return $$$$"""
        $ErrorActionPreference = 'Stop'
        $sessionId = '{{{{escapedSession}}}}'
        $outputRoot = '{{{{escapedOutput}}}}'
        $workingDirectory = '{{{{escapedWorking}}}}'
        $provisioningFailurePolicy = '{{{{failurePolicy}}}}'
        $metaRoot = Join-Path $outputRoot '.sandforge'
        $collectorRoot = Join-Path $metaRoot 'collectors'
        New-Item -ItemType Directory -Force -Path $outputRoot, $workingDirectory, $metaRoot, $collectorRoot | Out-Null
        $arguments = @(ConvertFrom-Json @'
        {{{{argsJson}}}}
        '@)
        $collectors = @(ConvertFrom-Json @'
        {{{{collectorsJson}}}}
        '@)
        $packages = @(ConvertFrom-Json @'
        {{{{packagesJson}}}}
        '@)
        $installers = @(ConvertFrom-Json @'
        {{{{installersJson}}}}
        '@)
        $cacheTypes = @(ConvertFrom-Json @'
        {{{{cacheTypesJson}}}}
        '@)

        if($cacheTypes -contains 'nuget'){ $env:NUGET_PACKAGES='C:\Sandbox\Cache\nuget' }
        if($cacheTypes -contains 'npm'){ $env:npm_config_cache='C:\Sandbox\Cache\npm' }
        if($cacheTypes -contains 'pip'){ $env:PIP_CACHE_DIR='C:\Sandbox\Cache\pip' }

        function Test-Collector([string]$name) { return $collectors -contains $name }
        function Capture-Snapshot([scriptblock]$action) {
          try { return [pscustomobject]@{ Items=@(& $action); Error=$null } }
          catch { return [pscustomobject]@{ Items=@(); Error=$_.Exception.Message } }
        }
        function Save-Collector([string]$name, $items, [object[]]$errors) {
          $errorText = (@($errors) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) -join ' | '
          if ([string]::IsNullOrWhiteSpace($errorText)) { $errorText = $null }
          $payload = [ordered]@{ collector=$name; items=@($items); error=$errorText }
          $fileName = if($name -in @('process-list','provisioning')){ "$name.json" }else{ "$name-diff.json" }
          $payload | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 (Join-Path $collectorRoot $fileName)
        }
        function Invoke-SandForgeProcess([string]$file, [object[]]$processArguments, [int]$timeoutSeconds) {
          $stdout = Join-Path $metaRoot ("process-{0}-stdout.txt" -f [Guid]::NewGuid().ToString('N'))
          $stderr = Join-Path $metaRoot ("process-{0}-stderr.txt" -f [Guid]::NewGuid().ToString('N'))
          try {
            $process = Start-Process -FilePath $file -ArgumentList $processArguments -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden
            if(-not $process.WaitForExit([Math]::Max(1,$timeoutSeconds) * 1000)){
              Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
              return [pscustomobject]@{ ExitCode=124; Error="Timeout ${timeoutSeconds}s"; StdOut=$stdout; StdErr=$stderr }
            }
            return [pscustomobject]@{ ExitCode=$process.ExitCode; Error=$null; StdOut=$stdout; StdErr=$stderr }
          } catch {
            return [pscustomobject]@{ ExitCode=1; Error=$_.Exception.Message; StdOut=$stdout; StdErr=$stderr }
          }
        }
        function Invoke-Provisioning {
          $results=@()
          foreach($package in $packages){
            $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
            if($null -eq $winget){
              $results += [pscustomobject]@{ Type='package'; Id=$package.id; Success=$false; ExitCode=127; Error='winget.exe не найден' }
              continue
            }
            $packageArgs=@('install','--id',[string]$package.id,'--exact','--silent','--disable-interactivity','--accept-package-agreements','--accept-source-agreements')
            if(-not [string]::IsNullOrWhiteSpace([string]$package.version)){ $packageArgs += @('--version',[string]$package.version) }
            if(-not [string]::IsNullOrWhiteSpace([string]$package.source)){ $packageArgs += @('--source',[string]$package.source) }
            $run=Invoke-SandForgeProcess $winget.Source $packageArgs 900
            $results += [pscustomobject]@{ Type='package'; Id=$package.id; Version=$package.version; Success=($run.ExitCode -eq 0); ExitCode=$run.ExitCode; Error=$run.Error; StdOut=$run.StdOut; StdErr=$run.StdErr }
          }
          foreach($installer in $installers){
            if(-not (Test-Path -LiteralPath $installer.path)){
              $results += [pscustomobject]@{ Type='installer'; Id=$installer.path; Success=$false; ExitCode=2; Error='Файл installer не найден' }
              continue
            }
            $actual=(Get-FileHash -LiteralPath $installer.path -Algorithm SHA256).Hash
            if($actual -ne [string]$installer.sha256){
              $results += [pscustomobject]@{ Type='installer'; Id=$installer.path; Success=$false; ExitCode=3; Error='SHA-256 не совпадает' }
              continue
            }
            $extension=[IO.Path]::GetExtension([string]$installer.path).ToLowerInvariant()
            $file=[string]$installer.path
            $installerArgs=@($installer.arguments)
            if($extension -eq '.msi'){
              $file='msiexec.exe'
              $installerArgs=@('/i',[string]$installer.path,'/qn','/norestart') + $installerArgs
            }
            $run=Invoke-SandForgeProcess $file $installerArgs ([int]$installer.timeoutSeconds)
            $results += [pscustomobject]@{ Type='installer'; Id=$installer.path; Success=($run.ExitCode -eq 0); ExitCode=$run.ExitCode; Error=$run.Error; StdOut=$run.StdOut; StdErr=$run.StdErr }
          }
          return @($results)
        }
        function Get-ProcessesSnapshot {
          @(Get-CimInstance Win32_Process -ErrorAction Stop | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CreationDate)
        }
        function Get-InstalledAppsSnapshot {
          $paths = @('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*')
          @($paths | ForEach-Object { Get-ItemProperty $_ -ErrorAction SilentlyContinue } | Where-Object DisplayName | Select-Object @{n='Id';e={"$($_.PSPath)|$($_.DisplayName)"}},DisplayName,DisplayVersion,Publisher,InstallLocation)
        }
        function Get-ServicesSnapshot {
          @(Get-CimInstance Win32_Service -ErrorAction Stop | Select-Object Name,DisplayName,State,StartMode,PathName)
        }
        function Get-TasksSnapshot {
          if (Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue) { @(Get-ScheduledTask -ErrorAction Stop | Select-Object @{n='Id';e={"$($_.TaskPath)$($_.TaskName)"}},TaskName,TaskPath,State) } else { @() }
        }
        function Get-FilesSnapshot {
          $roots = @($env:ProgramFiles,${env:ProgramFiles(x86)},$env:ProgramData,$env:LOCALAPPDATA) | Where-Object { $_ -and (Test-Path $_) }
          @($roots | ForEach-Object { Get-ChildItem $_ -File -Recurse -Force -ErrorAction SilentlyContinue } | Select-Object -First 50000 @{n='Path';e={$_.FullName}},Length,LastWriteTimeUtc)
        }
        function Get-RegistrySnapshot {
          $keys = @('HKLM:\Software\Microsoft\Windows\CurrentVersion\Run','HKCU:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall','HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall')
          $result = @()
          foreach ($key in $keys) {
            if (-not (Test-Path $key)) { continue }
            Get-ChildItem $key -Recurse -ErrorAction SilentlyContinue | Select-Object -First 20000 | ForEach-Object {
              $item = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
              if ($null -eq $item) { return }
              foreach ($property in $item.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' }) {
                $result += [pscustomobject]@{ Id="$($_.PSPath)|$($property.Name)"; Path=$_.PSPath; Name=$property.Name; Value=[string]$property.Value }
              }
            }
          }
          return @($result)
        }
        function Compare-Snapshot($before, $after, [string]$key) {
          $left=@{}; $right=@{}
          foreach($item in @($before)){ $id=[string]$item.$key; if(-not [string]::IsNullOrWhiteSpace($id)){ $left[$id]=$item } }
          foreach($item in @($after)){ $id=[string]$item.$key; if(-not [string]::IsNullOrWhiteSpace($id)){ $right[$id]=$item } }
          $changes=@()
          foreach($id in $right.Keys){
            if(-not $left.ContainsKey($id)){ $changes += [pscustomobject]@{Change='Added';Key=$id;After=$right[$id]} }
            elseif(($left[$id]|ConvertTo-Json -Compress -Depth 6) -ne ($right[$id]|ConvertTo-Json -Compress -Depth 6)){ $changes += [pscustomobject]@{Change='Modified';Key=$id;Before=$left[$id];After=$right[$id]} }
          }
          foreach($id in $left.Keys){ if(-not $right.ContainsKey($id)){ $changes += [pscustomobject]@{Change='Removed';Key=$id;Before=$left[$id]} } }
          return @($changes)
        }

        $provisioningResults=@()
        if($packages.Count -gt 0 -or $installers.Count -gt 0){
          $provisioningResults=Invoke-Provisioning
          $provisioningErrors=@($provisioningResults | Where-Object { -not $_.Success } | ForEach-Object { "$($_.Type):$($_.Id):$($_.Error)" })
          Save-Collector 'provisioning' $provisioningResults $provisioningErrors
        }
        $provisioningFailed=@($provisioningResults | Where-Object { -not $_.Success }).Count

        $before=@{}
        if(Test-Collector 'installed-apps'){ $before.apps = Capture-Snapshot { Get-InstalledAppsSnapshot } }
        if(Test-Collector 'services'){ $before.services = Capture-Snapshot { Get-ServicesSnapshot } }
        if(Test-Collector 'scheduled-tasks'){ $before.tasks = Capture-Snapshot { Get-TasksSnapshot } }
        if(Test-Collector 'file-changes'){ $before.files = Capture-Snapshot { Get-FilesSnapshot } }
        if(Test-Collector 'registry-changes'){ $before.registry = Capture-Snapshot { Get-RegistrySnapshot } }

        $startedAt=[DateTimeOffset]::UtcNow
        $exitCode=1
        $locationPushed=$false
        try {
          if($provisioningFailed -gt 0 -and $provisioningFailurePolicy -eq 'stop'){
            $exitCode=20
          } else {
            Push-Location $workingDirectory
            $locationPushed=$true
            & '{{{{escapedTarget}}}}' @arguments
            $exitCode=if($null -eq $LASTEXITCODE){0}else{[int]$LASTEXITCODE}
          }
        } catch {
          $_ | Out-String | Set-Content -Encoding UTF8 (Join-Path $metaRoot 'bootstrap-error.txt')
          $exitCode=1
        } finally {
          if($locationPushed){ Pop-Location -ErrorAction SilentlyContinue }

          if(Test-Collector 'process-list'){
            $after = Capture-Snapshot { Get-ProcessesSnapshot }
            Save-Collector 'process-list' $after.Items @($after.Error)
          }
          if(Test-Collector 'installed-apps'){
            $after = Capture-Snapshot { Get-InstalledAppsSnapshot }
            $items = if($before.apps.Error -or $after.Error){ @() }else{ Compare-Snapshot $before.apps.Items $after.Items 'Id' }
            Save-Collector 'installed-apps' $items @($before.apps.Error,$after.Error)
          }
          if(Test-Collector 'services'){
            $after = Capture-Snapshot { Get-ServicesSnapshot }
            $items = if($before.services.Error -or $after.Error){ @() }else{ Compare-Snapshot $before.services.Items $after.Items 'Name' }
            Save-Collector 'services' $items @($before.services.Error,$after.Error)
          }
          if(Test-Collector 'scheduled-tasks'){
            $after = Capture-Snapshot { Get-TasksSnapshot }
            $items = if($before.tasks.Error -or $after.Error){ @() }else{ Compare-Snapshot $before.tasks.Items $after.Items 'Id' }
            Save-Collector 'scheduled-tasks' $items @($before.tasks.Error,$after.Error)
          }
          if(Test-Collector 'file-changes'){
            $after = Capture-Snapshot { Get-FilesSnapshot }
            $items = if($before.files.Error -or $after.Error){ @() }else{ Compare-Snapshot $before.files.Items $after.Items 'Path' }
            Save-Collector 'file-changes' $items @($before.files.Error,$after.Error)
          }
          if(Test-Collector 'registry-changes'){
            $after = Capture-Snapshot { Get-RegistrySnapshot }
            $items = if($before.registry.Error -or $after.Error){ @() }else{ Compare-Snapshot $before.registry.Items $after.Items 'Id' }
            Save-Collector 'registry-changes' $items @($before.registry.Error,$after.Error)
          }

          $marker=[ordered]@{schemaVersion=1;sessionId=$sessionId;targetExitCode=$exitCode;provisioningFailed=$provisioningFailed;startedAt=$startedAt.ToString('O');endedAt=[DateTimeOffset]::UtcNow.ToString('O')}
          $marker | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $metaRoot 'completed.json')
          Start-Sleep -Seconds 1
          Start-Process shutdown.exe -ArgumentList '/s','/t','1' -WindowStyle Hidden
        }
        """;
    }

    private static string Ps(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
