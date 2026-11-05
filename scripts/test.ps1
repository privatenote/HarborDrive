param([switch]$Integration, [ValidateSet('host','arm64','x64')][string]$Architecture = 'host')
. "$PSScriptRoot\common.ps1"
. "$PSScriptRoot\dependencies.ps1"
Push-Location $RepoRoot
try {
    $TestArgs = @($RepoRoot)
    if ($Integration) { $TestArgs += '--integration' }
    if ($Architecture -eq 'host') {
        & $DotnetExe build 'tests\HarborDrive.Tests\HarborDrive.Tests.csproj' -c Release
        if ($LASTEXITCODE) { throw 'Test build failed.' }
        & $DotnetExe 'tests\HarborDrive.Tests\bin\Release\net10.0-windows\HarborDrive.Tests.dll' @TestArgs
    } else {
        $TestOutput = Join-Path $RepoRoot "artifacts\tests-win-$Architecture"
        & $DotnetExe publish 'tests\HarborDrive.Tests\HarborDrive.Tests.csproj' -c Release -r "win-$Architecture" --self-contained true -o $TestOutput
        if ($LASTEXITCODE) { throw 'Test publish failed.' }
        $env:HARBORDRIVE_TEST_RCLONE = Get-RcloneBinary $Architecture
        try { & (Join-Path $TestOutput 'HarborDrive.Tests.exe') @TestArgs }
        finally { Remove-Item Env:HARBORDRIVE_TEST_RCLONE -ErrorAction SilentlyContinue }
    }
    if ($LASTEXITCODE) { throw 'Tests failed. Inspect .devdata for preserved evidence.' }
} finally { Pop-Location }
