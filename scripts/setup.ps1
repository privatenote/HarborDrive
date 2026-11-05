param([switch]$InstallWinFsp, [switch]$SkipSdk)
. "$PSScriptRoot\common.ps1"
$ProgressPreference = 'SilentlyContinue'
$ToolsRoot = Join-Path $RepoRoot '.tools'
New-Item -ItemType Directory -Path $ToolsRoot,(Join-Path $RepoRoot 'tools') -Force | Out-Null
$OsArch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
if ($OsArch -notin @('arm64','x64')) { throw 'HarborDrive supports Windows ARM64 and x64.' }

function Download-Verified([string]$Url, [string]$Path, [string]$Hash, [string]$Algorithm = 'SHA256') {
    Invoke-WebRequest -Uri $Url -OutFile $Path
    if ((Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash -ne $Hash) { throw "Checksum mismatch: $Path" }
}

if (!$SkipSdk -and !(Test-Path -LiteralPath $DotnetExe)) {
    $SdkVersion = '10.0.403'
    $Metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
    $SdkFile = $Metadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $SdkVersion } | Select-Object -First 1 | ForEach-Object { $_.files } | Where-Object { $_.rid -eq "win-$OsArch" -and $_.name -like '*.zip' } | Select-Object -First 1
    if (!$SdkFile) { throw 'Pinned .NET SDK not found in official release metadata.' }
    $SdkZip = Join-Path $ToolsRoot 'dotnet-sdk.zip'
    Download-Verified $SdkFile.url $SdkZip $SdkFile.hash 'SHA512'
    Expand-Archive -LiteralPath $SdkZip -DestinationPath (Join-Path $ToolsRoot 'dotnet') -Force
    $DotnetExe = Join-Path $ToolsRoot 'dotnet\dotnet.exe'
}

$RcloneVersion = 'v1.75.2'
$RcloneArch = if ($OsArch -eq 'x64') { 'amd64' } else { 'arm64' }
$RcloneZipName = "rclone-$RcloneVersion-windows-$RcloneArch.zip"
$RcloneZip = Join-Path $ToolsRoot $RcloneZipName
$RcloneExe = Join-Path $RepoRoot 'tools\rclone.exe'
if (!(Test-Path -LiteralPath $RcloneExe)) {
    $Sums = Invoke-RestMethod "https://downloads.rclone.org/$RcloneVersion/SHA256SUMS"
    $HashLine = $Sums -split "`n" | Where-Object { $_ -match ([regex]::Escape($RcloneZipName) + '$') }
    if (!$HashLine) { throw 'rclone checksum not found.' }
    Download-Verified "https://downloads.rclone.org/$RcloneVersion/$RcloneZipName" $RcloneZip ($HashLine -split '\s+')[0]
    $ExtractDir = Join-Path $ToolsRoot 'rclone'
    Expand-Archive -LiteralPath $RcloneZip -DestinationPath $ExtractDir -Force
    Copy-Item -LiteralPath (Join-Path $ExtractDir "rclone-$RcloneVersion-windows-$RcloneArch\rclone.exe") -Destination $RcloneExe
}

if ($InstallWinFsp -and !(Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\WinFsp','HKLM:\SOFTWARE\WinFsp' -ErrorAction SilentlyContinue)) {
    $Installer = Join-Path $ToolsRoot 'winfsp-2.1.25156.msi'
    Invoke-WebRequest 'https://github.com/winfsp/winfsp/releases/download/v2.1/winfsp-2.1.25156.msi' -OutFile $Installer
    $Signature = Get-AuthenticodeSignature -LiteralPath $Installer
    if ($Signature.Status -ne 'Valid' -or $Signature.SignerCertificate.Subject -notmatch 'O=NAVIMATICS LLC') { throw 'WinFsp installer signature verification failed.' }
    $InstallLog = Join-Path $ToolsRoot 'winfsp-install.log'
    $InstallProcess = Start-Process msiexec.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('/i',('"' + $Installer + '"'),'/passive','/norestart','ADDLOCAL=ALL','/L*v',('"' + $InstallLog + '"')) -PassThru -Wait
    if ($InstallProcess.ExitCode -notin @(0,3010)) { throw "WinFsp installation failed: $($InstallProcess.ExitCode). See $InstallLog" }
    if ($InstallProcess.ExitCode -eq 3010) { Write-Host 'WinFsp requires a Windows restart.' }
}
& $RcloneExe version
if (!$SkipSdk) { & $DotnetExe --version; if ($LASTEXITCODE) { throw 'SDK version check failed.' } }
Write-Host 'Development dependencies are ready. Run scripts\build.ps1.'
