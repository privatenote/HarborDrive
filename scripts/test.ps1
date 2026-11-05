param([switch]$Integration)
. "$PSScriptRoot\common.ps1"
Push-Location $RepoRoot
try {
    & $DotnetExe build 'tests\HarborDrive.Tests\HarborDrive.Tests.csproj' -c Release
    if ($LASTEXITCODE) { throw 'Test build failed.' }
    $TestArgs = @('tests\HarborDrive.Tests\bin\Release\net10.0-windows\HarborDrive.Tests.dll', $RepoRoot)
    if ($Integration) { $TestArgs += '--integration' }
    & $DotnetExe @TestArgs
    if ($LASTEXITCODE) { throw 'Tests failed. Inspect .devdata for preserved evidence.' }
} finally { Pop-Location }
