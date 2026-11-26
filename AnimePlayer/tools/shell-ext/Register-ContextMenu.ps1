# Installs the AnimePlayer entries into the Windows 11 right-click menu (needs administrator rights ONCE: it trusts the package certificate).
# Run it again after you MOVE the player folder or install a newer build. Undo: Unregister-ContextMenu.bat.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $MyInvocation.MyCommand.Path + '"'))
    exit
}
$log = Join-Path $here 'register.log'
Start-Transcript -Path $log -Force | Out-Null
try {
    foreach ($f in @((Join-Path $root 'mpvnet.exe'), (Join-Path $root 'AnimePlayerShell.dll'), (Join-Path $here 'AnimePlayerShell.msix'), (Join-Path $here 'AnimePlayer.cer'))) {
        if (-not (Test-Path -LiteralPath $f)) { throw "Missing file: $f (this folder must stay inside the player folder, next to mpvnet.exe)" }
    }
    if ([Environment]::OSVersion.Version.Build -lt 22000) { Write-Host 'Note: this is not Windows 11, the entries will only show in the classic menu there.' }

    Write-Host 'Trusting the AnimePlayer package certificate ...'
    Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -eq 'CN=AnimePlayer' } | Remove-Item -Force
    Import-Certificate -FilePath (Join-Path $here 'AnimePlayer.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

    Write-Host 'Removing an older registration ...'
    Get-AppxPackage -Name 'AnimePlayer.ContextMenu' | Remove-AppxPackage -ErrorAction SilentlyContinue

    Write-Host "Registering for $root ..."
    Add-AppxPackage -Path (Join-Path $here 'AnimePlayerShell.msix') -ExternalLocation $root

    Write-Host 'Restarting Explorer (the taskbar flashes once) ...'
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
    Write-Host ''
    Write-Host 'Done. Right-click a video: "Play with AnimePlayer" and "Add to AnimePlayer playlist" are in the first menu.'
}
catch {
    Write-Host ''
    Write-Host ('ERROR: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host ('Details are in ' + $log + ' - send that file if you need help.')
    Write-Host 'Common causes: sideloading of apps is switched off (Settings > System > For developers), or the folder is on a network drive.'
}
finally {
    Stop-Transcript | Out-Null
    Read-Host 'Press Enter to close'
}
