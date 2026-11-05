$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $RepoRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:NUGET_PACKAGES = Join-Path $RepoRoot '.tools\nuget'
$DotnetExe = Join-Path $RepoRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $DotnetExe)) {
    $SiblingSdk = Join-Path (Split-Path -Parent $RepoRoot) '.tools\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $SiblingSdk) { $DotnetExe = $SiblingSdk }
    elseif (Get-Command dotnet -ErrorAction SilentlyContinue) { $DotnetExe = (Get-Command dotnet).Source }
}
