param([Parameter(Mandatory)][string]$SetupFile)
. "$PSScriptRoot\common.ps1"
. "$PSScriptRoot\dependencies.ps1"
if (!(New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this installation test with administrator privileges.' }
if (Get-Process HarborDrive -ErrorAction SilentlyContinue) { throw 'Close HarborDrive normally before testing the installer.' }
$UninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7BFE6434-7206-4B51-BCC4-E0D9511D13B4}_is1'
if (Test-Path -LiteralPath $UninstallKey) { throw 'An installed HarborDrive is already registered. Use a clean test environment; this script will not replace it.' }
$SetupFile = (Resolve-Path -LiteralPath $SetupFile).Path
$TestRoot = Join-Path $RepoRoot ('.devdata\installer-' + [guid]::NewGuid().ToString('N'))
$InstallDir = Join-Path $TestRoot 'product'
$ResolvedInstallDir = [System.IO.Path]::GetFullPath($InstallDir)
if (!$ResolvedInstallDir.StartsWith(([System.IO.Path]::GetFullPath((Join-Path $RepoRoot '.devdata')) + '\'), [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Installer test target is outside the project test directory.' }
$UserData = Join-Path $TestRoot 'userdata'
New-Item -ItemType Directory -Path $UserData -Force | Out-Null
$Sentinel = Join-Path $UserData 'cache-must-survive.txt'
[System.IO.File]::WriteAllText($Sentinel, 'Unsent cache sentinel: preserve on uninstall.')
$Transcript = Join-Path $RepoRoot '.devdata\installer-test-transcript.log'
Start-Transcript -LiteralPath $Transcript -Force | Out-Null
$EngineProcess = $null
$AppProcess = $null
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunRegistryPath = 'HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run'
$PreviousRun = [Microsoft.Win32.Registry]::GetValue($RunRegistryPath, 'HarborDrive', $null)
$BeforeRunKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
$OtherStartupValues = @{}
if ($BeforeRunKey) {
    try { foreach ($Name in $BeforeRunKey.GetValueNames()) { if ($Name -ne 'HarborDrive') { $OtherStartupValues[$Name] = $BeforeRunKey.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } } }
    finally { $BeforeRunKey.Dispose() }
}
$OtherStartupValues | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $TestRoot 'startup-values-before.json') -Encoding utf8
$WinFspDirectory = Get-ItemPropertyValue 'HKLM:\SOFTWARE\WOW6432Node\WinFsp' -Name InstallDir -ErrorAction SilentlyContinue
$CheckCount = 0
function Set-RunValue($Value) {
    $StartupKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try {
        if ($null -eq $Value) { $StartupKey.DeleteValue('HarborDrive', $false) }
        else { $StartupKey.SetValue('HarborDrive', $Value, [Microsoft.Win32.RegistryValueKind]::String) }
    } finally { $StartupKey.Dispose() }
}
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw "FAIL: $Name" }
    Write-Host "PASS: $Name"
    $script:CheckCount++
}
function Run-Setup([string]$Name) {
    $InstallLog = Join-Path $TestRoot "$Name.log"
    $Process = Start-Process $SetupFile -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/RESTARTEXITCODE=3010',('/DIR="' + $InstallDir + '"'),('/LOG="' + $InstallLog + '"')) -WindowStyle Hidden -PassThru -Wait
    return $Process.ExitCode
}
function Run-Uninstall([string]$Name) {
    $LogPath = Join-Path $TestRoot "$Name.log"
    $Process = Start-Process (Join-Path $InstallDir 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + $LogPath + '"')) -WindowStyle Hidden -PassThru -Wait
    return $Process.ExitCode
}
try {
    Check ((Run-Setup 'first-install') -in @(0,3010)) 'offline single-file install succeeded'
    foreach ($File in @('HarborDrive.exe','coreclr.dll','tools\rclone.exe')) {
        Check ((Get-PeMachine (Join-Path $InstallDir $File)) -eq 0x8664) "$File is x86-64"
    }
    Check (Test-Path -LiteralPath (Join-Path $InstallDir 'unins000.exe')) 'uninstaller created'
    Check ((Get-ItemPropertyValue -LiteralPath $UninstallKey -Name InstallLocation).TrimEnd('\') -eq $InstallDir) '64-bit uninstall registration points to test installation'
    Check (Test-Path -LiteralPath (Join-Path $InstallDir 'licenses\WINFSP-LICENSE.txt')) 'WinFsp license included'
    $Info = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $InstallDir 'tools\rclone.exe'))
    $Info.UseShellExecute = $false; $Info.CreateNoWindow = $true
    foreach ($Arg in @('rcd','--rc-addr','127.0.0.1:0','--config','NUL','--log-file',(Join-Path $TestRoot 'engine.log'))) { $Info.ArgumentList.Add($Arg) }
    $EngineProcess = [System.Diagnostics.Process]::Start($Info)
    Start-Sleep -Milliseconds 1200
    Check ((Run-Setup 'running-engine-install-blocked') -notin @(0,3010)) 'update refuses a live installed engine'
    Check ((Run-Uninstall 'running-engine-uninstall-blocked') -ne 0) 'uninstall refuses a live installed engine'
    Check (!$EngineProcess.HasExited) 'blocked operations do not terminate engine'
    $EngineProcess.Kill(); $EngineProcess.WaitForExit(); $EngineProcess.Dispose(); $EngineProcess = $null
    Check ((Run-Setup 'repeat-install') -in @(0,3010)) 'repeat install succeeds without reinstalling shared driver'
    $SmokeInfo = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $InstallDir 'HarborDrive.exe'))
    $SmokeInfo.UseShellExecute = $false; $SmokeInfo.CreateNoWindow = $true; $SmokeInfo.Environment['HARBORDRIVE_HOME'] = $UserData
    $SmokeInfo.ArgumentList.Add('--smoke-test')
    $AppProcess = [System.Diagnostics.Process]::Start($SmokeInfo)
    if (!$AppProcess.WaitForExit(20000)) { throw 'App resource smoke check timed out.' }
    Check ($AppProcess.ExitCode -eq 0) 'installed app loads window and all tray icon resources'
    $AppProcess.Dispose(); $AppProcess = $null
    $AppInfo = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $InstallDir 'HarborDrive.exe'))
    $AppInfo.UseShellExecute = $false; $AppInfo.CreateNoWindow = $true; $AppInfo.Environment['HARBORDRIVE_HOME'] = $UserData
    $AppInfo.ArgumentList.Add('--background')
    $AppProcess = [System.Diagnostics.Process]::Start($AppInfo)
    Start-Sleep -Milliseconds 2000
    Check (!$AppProcess.HasExited) 'installed x64 app starts without separate .NET installation'
    Check ((Run-Uninstall 'running-app-uninstall-blocked') -ne 0) 'uninstall refuses a live app'
    # This app uses a new empty test profile, with no drives or user data open.
    $AppProcess.Kill(); $AppProcess.WaitForExit(); $AppProcess.Dispose(); $AppProcess = $null
    Set-RunValue ('"' + (Join-Path $InstallDir 'HarborDrive.exe') + '" --background')
    Check ((Run-Uninstall 'clean-uninstall') -eq 0) 'clean uninstall succeeds'
    Check (!(Test-Path -LiteralPath (Join-Path $InstallDir 'HarborDrive.exe'))) 'installed app removed'
    Check (!(Test-Path -LiteralPath $UninstallKey)) 'uninstall registration removed'
    Check (Test-Path -LiteralPath $Sentinel) 'user cache retained'
    Check ($null -eq [Microsoft.Win32.Registry]::GetValue($RunRegistryPath, 'HarborDrive', $null)) 'matching startup entry removed'
    $AfterRunKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try {
        foreach ($Name in $OtherStartupValues.Keys) {
            Check ($AfterRunKey -and ($AfterRunKey.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) -eq $OtherStartupValues[$Name])) "unrelated startup value retained: $Name"
        }
    } finally { if ($AfterRunKey) { $AfterRunKey.Dispose() } }
    if ($WinFspDirectory) { Check (Test-Path -LiteralPath (Join-Path $WinFspDirectory 'bin\winfsp-x64.dll')) 'shared WinFsp preserved' }
    Write-Host "All $CheckCount installer checks passed. Evidence: $TestRoot"
} catch {
    Write-Host "INSTALLER TEST FAILED: $_"
    throw
} finally {
    foreach ($OwnedProcess in @($EngineProcess,$AppProcess)) { if ($OwnedProcess -and !$OwnedProcess.HasExited) { $OwnedProcess.Kill(); $OwnedProcess.WaitForExit() }; if ($OwnedProcess) { $OwnedProcess.Dispose() } }
    Set-RunValue $PreviousRun
    Stop-Transcript | Out-Null
}
