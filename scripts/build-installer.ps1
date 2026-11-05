param([switch]$SkipPublish, [string]$OutputDirectory)
. "$PSScriptRoot\common.ps1"
. "$PSScriptRoot\dependencies.ps1"
Push-Location $RepoRoot
try {
    if (!$SkipPublish) {
        & "$PSScriptRoot\build.ps1" -Architecture x64 -Publish
        if (!$?) { throw 'x64 publish failed.' }
    }
    $Payload = Join-Path $RepoRoot 'artifacts\HarborDrive-win-x64'
    foreach ($File in @('HarborDrive.exe','coreclr.dll','tools\rclone.exe')) {
        if ((Get-PeMachine (Join-Path $Payload $File)) -ne 0x8664) { throw "Payload is not x86-64: $File" }
    }
    $WinFsp = Get-WinFspInstaller
    $Compiler = Get-InnoCompiler
    $Licenses = Join-Path $Payload 'licenses'
    $WinFspLicense = Join-Path $RepoRoot 'tools\WINFSP-LICENSE.txt'
    if (!(Test-Path -LiteralPath $WinFspLicense)) {
        Invoke-WebRequest 'https://raw.githubusercontent.com/winfsp/winfsp/v2.1/License.txt' -OutFile $WinFspLicense
    }
    Copy-Item -LiteralPath $WinFspLicense -Destination (Join-Path $Licenses 'WINFSP-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $Compiler) 'license.txt') -Destination (Join-Path $Licenses 'INNO-SETUP-LICENSE.txt')
    [xml]$Project = Get-Content 'src\HarborDrive.App\HarborDrive.App.csproj' -Raw
    $Version = [string]$Project.Project.PropertyGroup.Version
    if (!$OutputDirectory) { $OutputDirectory = Join-Path $RepoRoot 'artifacts' }
    $OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    & $Compiler /Qp "/O$OutputDirectory" "/DPayloadDir=$Payload" "/DWinFspMsi=$WinFsp" "/DAppVersion=$Version" 'installer\HarborDrive.iss'
    if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
    $SetupFile = Join-Path $OutputDirectory "HarborDrive-$Version-x64-Setup.exe"
    if ((Get-PeMachine $SetupFile) -ne 0x8664) { throw 'Setup bootstrapper is not x86-64.' }
    $Hash = (Get-FileHash -LiteralPath $SetupFile -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText("$SetupFile.sha256", "$Hash  $([System.IO.Path]::GetFileName($SetupFile))`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host "Offline installer ready: $SetupFile"
    Write-Host "SHA256: $Hash"
} finally { Pop-Location }
