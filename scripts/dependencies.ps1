# Dot-source common.ps1 before this file.
function Get-PeMachine([string]$Path) {
    $Stream = [System.IO.File]::OpenRead($Path)
    $Reader = [System.IO.BinaryReader]::new($Stream)
    try {
        if ($Reader.ReadUInt16() -ne 0x5A4D) { throw "Not a PE file: $Path" }
        $Stream.Position = 0x3C
        $PeOffset = $Reader.ReadInt32()
        $Stream.Position = $PeOffset
        if ($Reader.ReadUInt32() -ne 0x4550) { throw "Invalid PE signature: $Path" }
        return $Reader.ReadUInt16()
    } finally { $Reader.Dispose(); $Stream.Dispose() }
}

function Get-RcloneBinary([ValidateSet('arm64','x64')][string]$Architecture) {
    $NativeArch = if ($Architecture -eq 'x64') { 'amd64' } else { 'arm64' }
    $Version = 'v1.75.2'
    $ExpectedHash = if ($Architecture -eq 'x64') { '8a9db2bad1dc79a65b3348fd857069598e2aab7cf4d118b77b1df23055aff818' } else { '7eb9844c9e5b723b9ef28821891caa17f6d9d1a375c49365cbe04114cec8dcd9' }
    $ArchiveName = "rclone-$Version-windows-$NativeArch.zip"
    $Cache = Join-Path $RepoRoot ".tools\rclone\$Version\$Architecture"
    $Executable = Join-Path $Cache "rclone-$Version-windows-$NativeArch\rclone.exe"
    if (!(Test-Path -LiteralPath $Executable)) {
        New-Item -ItemType Directory -Path $Cache -Force | Out-Null
        $Archive = Join-Path $Cache $ArchiveName
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest "https://downloads.rclone.org/$Version/$ArchiveName" -OutFile $Archive
        if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ne $ExpectedHash) { throw 'rclone checksum mismatch.' }
        Expand-Archive -LiteralPath $Archive -DestinationPath $Cache -Force
    }
    $ExpectedMachine = if ($Architecture -eq 'x64') { 0x8664 } else { 0xAA64 }
    if ((Get-PeMachine $Executable) -ne $ExpectedMachine) { throw 'rclone PE architecture mismatch.' }
    return $Executable
}

function Get-WinFspInstaller {
    $Cache = Join-Path $RepoRoot '.tools\installer'
    New-Item -ItemType Directory -Path $Cache -Force | Out-Null
    $Installer = Join-Path $Cache 'winfsp-2.1.25156.msi'
    if (!(Test-Path -LiteralPath $Installer)) {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest 'https://github.com/winfsp/winfsp/releases/download/v2.1/winfsp-2.1.25156.msi' -OutFile $Installer
    }
    $Signature = Get-AuthenticodeSignature -LiteralPath $Installer
    if ((Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash -ne '073a70e00f77423e34bed98b86e600def93393ba5822204fac57a29324db9f7a') { throw 'WinFsp installer checksum mismatch.' }
    if ($Signature.Status -ne 'Valid' -or $Signature.SignerCertificate.Subject -notmatch 'O=NAVIMATICS LLC') { throw 'WinFsp installer signature verification failed.' }
    return $Installer
}

function Get-InnoCompiler {
    $Cache = Join-Path $RepoRoot '.tools\installer'
    $Compiler = Join-Path $Cache 'inno\ISCC.exe'
    if (!(Test-Path -LiteralPath $Compiler)) {
        New-Item -ItemType Directory -Path $Cache -Force | Out-Null
        $Installer = Join-Path $Cache 'innosetup-7.1.1-x64.exe'
        $ProgressPreference = 'SilentlyContinue'
        if (!(Test-Path -LiteralPath $Installer)) { Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' -OutFile $Installer }
        if ((Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash -ne '8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0') { throw 'Inno Setup checksum mismatch.' }
        $Signature = Get-AuthenticodeSignature -LiteralPath $Installer
        if ($Signature.Status -ne 'Valid' -or $Signature.SignerCertificate.Subject -notmatch 'Pyrsys') { throw 'Inno Setup signature verification failed.' }
        $Target = Join-Path $Cache 'inno'
        $Process = Start-Process $Installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS',('/DIR="' + $Target + '"')) -WindowStyle Hidden -PassThru -Wait
        if ($Process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($Process.ExitCode)" }
    }
    return $Compiler
}
