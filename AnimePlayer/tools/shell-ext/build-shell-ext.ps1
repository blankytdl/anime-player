# Builds the Windows 11 right-click menu extension (see AnimePlayerShell.cpp). Runs in CI (windows-latest has MSVC + the Windows SDK).
#   -Dist   the player output folder (dist)       -Build  the build number (BUILD.txt)
# Output:   dist\AnimePlayerShell.dll                 (next to mpvnet.exe)
#           dist\context-menu\AnimePlayerShell.msix   (signed sparse package: identity only)
#           dist\context-menu\AnimePlayer.cer         (the certificate to trust), Register-/Unregister-ContextMenu (.ps1 + .bat), README.txt
# Needs cl.exe on PATH (ilammy/msvc-dev-cmd in the workflow, or a "x64 Native Tools" prompt) and makeappx.exe / signtool.exe from the Windows SDK.
param(
    [Parameter(Mandatory = $true)][string]$Dist,
    [string]$Build = '0'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$work = Join-Path $here '_build'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null
$Dist = (Resolve-Path -LiteralPath $Dist).Path
$ctx = Join-Path $Dist 'context-menu'
New-Item -ItemType Directory -Force -Path $ctx | Out-Null
$Build = ($Build -replace '[^0-9]', ''); if ($Build -eq '') { $Build = '0' }

function Find-SdkTool([string]$name) {
    $c = Get-Command $name -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    $roots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:ProgramFiles\Windows Kits\10\bin") | Where-Object { Test-Path $_ }
    foreach ($r in $roots) {
        $hit = Get-ChildItem -LiteralPath $r -Directory | Where-Object { $_.Name -like '10.*' } | Sort-Object { [version]$_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\$name" } | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($hit) { return $hit }
    }
    throw "$name was not found (install the Windows 10/11 SDK)."
}

# ---------------------------------------------------------------- 1. compile the DLL (static runtime: no VC++ redistributable needed)
if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) { throw 'cl.exe is not on PATH (open an x64 Native Tools prompt or use ilammy/msvc-dev-cmd).' }
$cpp = Join-Path $here 'AnimePlayerShell.cpp'
$def = Join-Path $here 'AnimePlayerShell.def'
$dll = Join-Path $work 'AnimePlayerShell.dll'
& cl.exe /nologo /std:c++17 /O2 /EHsc /MT /LD /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN $cpp "/Fo$work\AnimePlayerShell.obj" "/Fe$dll" /link "/DEF:$def" runtimeobject.lib shlwapi.lib shell32.lib ole32.lib
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dll)) { throw "cl.exe failed (exit $LASTEXITCODE)." }

# ---------------------------------------------------------------- 2. manifest (CLSIDs come from the C++ source)
$src = Get-Content -LiteralPath $cpp -Raw
$ids = [regex]::Matches($src, 'DEFINE_COMMAND\(\w+,\s*"([0-9A-Fa-f\-]{36})"') | ForEach-Object { $_.Groups[1].Value.ToUpper() }
if ($ids.Count -ne 2) { throw "Expected 2 DEFINE_COMMAND CLSIDs in AnimePlayerShell.cpp, found $($ids.Count)." }
$video = 'mkv mp4 avi mov flv mpg webm wmv ts vob 264 265 asf avc avs dav h264 h265 hevc m2t m2ts m2v m4v mpeg mpv mts y4m'
$audio = 'mp3 flac m4a mka mp2 ogg opus aac ac3 dts dtshd dtshr dtsma eac3 mpa mpc thd w64 wav'
$types = ($video + ' ' + $audio).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
$itemTypes = ($types | ForEach-Object {
    "            <desktop5:ItemType Type=`".$_`">`n              <desktop5:Verb Id=`"AnimePlayerPlay`" Clsid=`"$($ids[0])`" />`n              <desktop5:Verb Id=`"AnimePlayerAdd`" Clsid=`"$($ids[1])`" />`n            </desktop5:ItemType>"
}) -join "`n"
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
  xmlns:desktop4="http://schemas.microsoft.com/appx/manifest/desktop/windows10/4"
  xmlns:desktop5="http://schemas.microsoft.com/appx/manifest/desktop/windows10/5"
  xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap10 desktop4 desktop5 com rescap">
  <Identity Name="AnimePlayer.ContextMenu" Publisher="CN=AnimePlayer" Version="1.0.$Build.0" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>AnimePlayer context menu</DisplayName>
    <PublisherDisplayName>AnimePlayer</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
    <uap10:AllowExternalContent>true</uap10:AllowExternalContent>
  </Properties>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
    <rescap:Capability Name="unvirtualizedResources" />
  </Capabilities>
  <Applications>
    <Application Id="AnimePlayer" Executable="mpvnet.exe" uap10:TrustLevel="mediumIL" uap10:RuntimeBehavior="win32App">
      <uap:VisualElements AppListEntry="none" DisplayName="AnimePlayer" Description="AnimePlayer right-click menu" BackgroundColor="transparent"
                          Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" />
      <Extensions>
        <desktop4:Extension Category="windows.fileExplorerContextMenus">
          <desktop4:FileExplorerContextMenus>
$itemTypes
          </desktop4:FileExplorerContextMenus>
        </desktop4:Extension>
        <com:Extension Category="windows.comServer">
          <com:ComServer>
            <com:SurrogateServer DisplayName="AnimePlayer right-click menu">
              <com:Class Id="$($ids[0])" Path="AnimePlayerShell.dll" ThreadingModel="STA" />
              <com:Class Id="$($ids[1])" Path="AnimePlayerShell.dll" ThreadingModel="STA" />
            </com:SurrogateServer>
          </com:ComServer>
        </com:Extension>
      </Extensions>
    </Application>
  </Applications>
</Package>
"@
$pkg = Join-Path $work 'pkg'
New-Item -ItemType Directory -Force -Path (Join-Path $pkg 'Assets') | Out-Null
[System.IO.File]::WriteAllText((Join-Path $pkg 'AppxManifest.xml'), $manifest, (New-Object System.Text.UTF8Encoding($false)))

# Validate the generated XML and manifest GUID syntax before invoking MakeAppx.
try { [xml]$manifestXml = Get-Content -LiteralPath (Join-Path $pkg 'AppxManifest.xml') -Raw }
catch { throw "Generated AppxManifest.xml is not well-formed XML: $($_.Exception.Message)" }
foreach ($id in $ids) {
    if ($id -notmatch '^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}

# logos (plain coloured squares; the package never shows in the Start menu: AppListEntry="none")
Add-Type -AssemblyName System.Drawing
foreach ($a in @(@('StoreLogo', 50), @('Square44x44Logo', 44), @('Square150x150Logo', 150))) {
    $bmp = New-Object System.Drawing.Bitmap([int]$a[1], [int]$a[1])
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 124, 77, 255))
    $g.Dispose()
    $bmp.Save((Join-Path $pkg ("Assets\" + $a[0] + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# ---------------------------------------------------------------- 3. pack + sign (a fresh self-signed certificate per build; Register-ContextMenu.ps1 trusts it)
$makeappx = Find-SdkTool 'makeappx.exe'
$signtool = Find-SdkTool 'signtool.exe'
$msix = Join-Path $work 'AnimePlayerShell.msix'
& $makeappx pack /d $pkg /p $msix /nv /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed (exit $LASTEXITCODE)." }

$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=AnimePlayer' -KeyUsage DigitalSignature -FriendlyName 'AnimePlayer right-click menu' `
    -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(10) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
try {
    $cer = Join-Path $work 'AnimePlayer.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    $pfxPass = [guid]::NewGuid().ToString('N')
    $pfx = Join-Path $work 'sign.pfx'
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString -String $pfxPass -Force -AsPlainText) | Out-Null
    & $signtool sign /fd SHA256 /f $pfx /p $pfxPass $msix
    if ($LASTEXITCODE -ne 0) { throw "signtool failed (exit $LASTEXITCODE)." }
    Remove-Item -LiteralPath $pfx -Force
}
finally {
    Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- 4. publish into dist
Copy-Item -LiteralPath $dll -Destination (Join-Path $Dist 'AnimePlayerShell.dll') -Force
Copy-Item -LiteralPath $msix -Destination $ctx -Force
Copy-Item -LiteralPath $cer -Destination $ctx -Force
foreach ($f in 'Register-ContextMenu.ps1', 'Unregister-ContextMenu.ps1', 'Register-ContextMenu.bat', 'Unregister-ContextMenu.bat', 'README.txt') {
    Copy-Item -LiteralPath (Join-Path $here $f) -Destination $ctx -Force
}
Remove-Item -LiteralPath $work -Recurse -Force
Write-Host "Context-menu extension built: $($types.Count) file types, package version 1.0.$Build.0"
) {
        throw "Invalid unbraced COM CLSID in generated AppxManifest.xml: $id"
    }
}
if ($manifest -match 'Clsid="\{') { throw 'Generated AppxManifest.xml contains a braced CLSID; AppX manifest GUID attributes must be unbraced.' }

# logos (plain coloured squares; the package never shows in the Start menu: AppListEntry="none")
Add-Type -AssemblyName System.Drawing
foreach ($a in @(@('StoreLogo', 50), @('Square44x44Logo', 44), @('Square150x150Logo', 150))) {
    $bmp = New-Object System.Drawing.Bitmap([int]$a[1], [int]$a[1])
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 124, 77, 255))
    $g.Dispose()
    $bmp.Save((Join-Path $pkg ("Assets\" + $a[0] + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# ---------------------------------------------------------------- 3. pack + sign (a fresh self-signed certificate per build; Register-ContextMenu.ps1 trusts it)
$makeappx = Find-SdkTool 'makeappx.exe'
$signtool = Find-SdkTool 'signtool.exe'
$msix = Join-Path $work 'AnimePlayerShell.msix'
& $makeappx pack /d $pkg /p $msix /nv /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed (exit $LASTEXITCODE)." }

$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=AnimePlayer' -KeyUsage DigitalSignature -FriendlyName 'AnimePlayer right-click menu' `
    -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(10) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
try {
    $cer = Join-Path $work 'AnimePlayer.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    $pfxPass = [guid]::NewGuid().ToString('N')
    $pfx = Join-Path $work 'sign.pfx'
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString -String $pfxPass -Force -AsPlainText) | Out-Null
    & $signtool sign /fd SHA256 /f $pfx /p $pfxPass $msix
    if ($LASTEXITCODE -ne 0) { throw "signtool failed (exit $LASTEXITCODE)." }
    Remove-Item -LiteralPath $pfx -Force
}
finally {
    Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- 4. publish into dist
Copy-Item -LiteralPath $dll -Destination (Join-Path $Dist 'AnimePlayerShell.dll') -Force
Copy-Item -LiteralPath $msix -Destination $ctx -Force
Copy-Item -LiteralPath $cer -Destination $ctx -Force
foreach ($f in 'Register-ContextMenu.ps1', 'Unregister-ContextMenu.ps1', 'Register-ContextMenu.bat', 'Unregister-ContextMenu.bat', 'README.txt') {
    Copy-Item -LiteralPath (Join-Path $here $f) -Destination $ctx -Force
}
Remove-Item -LiteralPath $work -Recurse -Force
Write-Host "Context-menu extension built: $($types.Count) file types, package version 1.0.$Build.0"
