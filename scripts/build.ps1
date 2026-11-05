param([ValidateSet('arm64','x64')][string]$Architecture = 'arm64', [switch]$Publish)
. "$PSScriptRoot\common.ps1"
. "$PSScriptRoot\dependencies.ps1"
Push-Location $RepoRoot
try {
    if (!(Test-Path -LiteralPath $DotnetExe)) { throw 'Run scripts\setup.ps1 first.' }
    & $DotnetExe build 'src\HarborDrive.App\HarborDrive.App.csproj' -c Release
    if ($LASTEXITCODE) { throw 'Build failed.' }
    if ($Publish) {
        $PublishPath = Join-Path $RepoRoot "artifacts\HarborDrive-win-$Architecture"
        $TargetRclone = Get-RcloneBinary $Architecture
        & $DotnetExe publish 'src\HarborDrive.App\HarborDrive.App.csproj' -c Release -r "win-$Architecture" --self-contained true -o $PublishPath -p:PublishSingleFile=false "-p:RcloneExecutable=$TargetRclone"
        if ($LASTEXITCODE) { throw 'Publish failed.' }
        $ExpectedMachine = if ($Architecture -eq 'x64') { 0x8664 } else { 0xAA64 }
        foreach ($Binary in @('HarborDrive.exe', 'coreclr.dll', 'tools\rclone.exe')) {
            if ((Get-PeMachine (Join-Path $PublishPath $Binary)) -ne $ExpectedMachine) { throw "Wrong architecture in package: $Binary" }
        }
        Copy-Item -LiteralPath (Join-Path $RepoRoot 'README.md'),(Join-Path $RepoRoot 'THIRD-PARTY-NOTICES.md') -Destination $PublishPath
        Copy-Item -LiteralPath (Join-Path $RepoRoot 'docs') -Destination $PublishPath -Recurse -Force
        $RuntimeConfig = Get-Content -LiteralPath (Join-Path $PublishPath 'HarborDrive.runtimeconfig.json') -Raw | ConvertFrom-Json
        $RuntimeVersion = ($RuntimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' }).version
        $RuntimePackage = Join-Path $env:NUGET_PACKAGES "microsoft.netcore.app.runtime.win-$Architecture\$RuntimeVersion"
        $DesktopPackage = Join-Path $env:NUGET_PACKAGES "microsoft.windowsdesktop.app.runtime.win-$Architecture\$RuntimeVersion"
        $Licenses = Join-Path $PublishPath 'licenses'
        New-Item -ItemType Directory -Path $Licenses -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $RuntimePackage 'LICENSE.TXT') -Destination (Join-Path $Licenses 'DOTNET-RUNTIME-LICENSE.txt')
        Copy-Item -LiteralPath (Join-Path $RuntimePackage 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $Licenses 'DOTNET-RUNTIME-NOTICES.txt')
        Copy-Item -LiteralPath (Join-Path $DesktopPackage 'LICENSE') -Destination (Join-Path $Licenses 'DOTNET-DESKTOP-LICENSE.txt')
        Compress-Archive -Path (Join-Path $PublishPath '*') -DestinationPath "$PublishPath.zip" -Force
        Write-Host "Ready: $PublishPath\HarborDrive.exe"
    }
} finally { Pop-Location }
