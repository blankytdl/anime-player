# Download and verify all four runtime dependencies required by AnimePlayer.
# The mpv packages are deliberately the generic x86-64 builds, never x86_64-v3.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$DownloadRoot = Join-Path $ProjectRoot '.ci-downloads'
New-Item -ItemType Directory -Force -Path $DownloadRoot | Out-Null

function Clear-Folder([string]$Path) {
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
}

function Download-File([string]$Url, [string]$Destination) {
    Write-Host "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Destination -Headers @{ 'User-Agent' = 'AnimePlayer-CPU-safe-build' } -MaximumRedirection 10
    if (-not (Test-Path -LiteralPath $Destination -PathType Leaf)) {
        throw "Download did not create $Destination"
    }
    if ((Get-Item -LiteralPath $Destination).Length -lt 1024) {
        throw "Downloaded file is unexpectedly small: $Destination"
    }
}

function Verify-Sha256([string]$Path, [string]$Expected, [string]$Label) {
    $Actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $Expected = $Expected.ToLowerInvariant()
    if ($Actual -ne $Expected) {
        throw "$Label SHA-256 mismatch. Expected $Expected; got $Actual."
    }
    Write-Host "[verified] $Label SHA-256 $Actual"
    return $Actual
}

$SevenZip = (Get-Command '7z.exe' -ErrorAction SilentlyContinue).Source
if (-not $SevenZip) {
    $Candidates = @(
        (Join-Path $env:ProgramFiles '7-Zip\7z.exe'),
        (Join-Path ${env:ProgramFiles(x86)} '7-Zip\7z.exe')
    )
    $SevenZip = $Candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $SevenZip) {
    if (Get-Command choco.exe -ErrorAction SilentlyContinue) {
        & choco.exe install 7zip.install -y --no-progress
        if ($LASTEXITCODE -ne 0) { throw 'Could not install 7-Zip for dependency extraction.' }
        $SevenZip = (Get-Command '7z.exe' -ErrorAction SilentlyContinue).Source
    }
}
if (-not $SevenZip) { throw '7z.exe is required to extract the verified mpv and FFmpeg packages.' }

# 1 + 2. Standard Shinchiro x86-64 libmpv development package and standalone mpv.
# x86_64-v3 is intentionally excluded: it requires AVX2/BMI2/FMA and other newer ISA features.
$Release = Invoke-RestMethod -Uri 'https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest' -Headers @{ 'User-Agent' = 'AnimePlayer-CPU-safe-build' }
$DevAssets = @($Release.assets | Where-Object {
    $_.name -match '^mpv-dev-x86_64-\d{8}-.+\.7z$' -and $_.name -notmatch '-v3-'
})
$MpvAssets = @($Release.assets | Where-Object {
    $_.name -match '^mpv-x86_64-\d{8}-.+\.7z$' -and $_.name -notmatch '-v3-'
})
if ($DevAssets.Count -ne 1) { throw "Expected one generic x86-64 mpv-dev asset in $($Release.tag_name); found $($DevAssets.Count)." }
if ($MpvAssets.Count -ne 1) { throw "Expected one generic x86-64 mpv asset in $($Release.tag_name); found $($MpvAssets.Count)." }

$DevAsset = $DevAssets[0]
$MpvAsset = $MpvAssets[0]
foreach ($Asset in @($DevAsset, $MpvAsset)) {
    if ($Asset.name -match 'v3|avx2') { throw "Refusing CPU-incompatible mpv package: $($Asset.name)" }
    if (-not $Asset.digest -or $Asset.digest -notmatch '^sha256:([0-9a-fA-F]{64})$') {
        throw "GitHub did not provide a SHA-256 digest for $($Asset.name); refusing an unverified download."
    }
}

$DevArchive = Join-Path $DownloadRoot $DevAsset.name
$MpvArchive = Join-Path $DownloadRoot $MpvAsset.name
Download-File $DevAsset.browser_download_url $DevArchive
Download-File $MpvAsset.browser_download_url $MpvArchive
$DevHash = Verify-Sha256 $DevArchive ($DevAsset.digest -replace '^sha256:', '') 'libmpv development package'
$MpvHash = Verify-Sha256 $MpvArchive ($MpvAsset.digest -replace '^sha256:', '') 'standalone mpv package'

$DevExtract = Join-Path $DownloadRoot 'mpv-dev-extracted'
$MpvExtract = Join-Path $DownloadRoot 'mpv-extracted'
Clear-Folder $DevExtract
Clear-Folder $MpvExtract
& $SevenZip x $DevArchive "-o$DevExtract" -y
if ($LASTEXITCODE -ne 0) { throw "Could not extract $($DevAsset.name)." }
& $SevenZip x $MpvArchive "-o$MpvExtract" -y
if ($LASTEXITCODE -ne 0) { throw "Could not extract $($MpvAsset.name)." }

$LibMpvFiles = @(Get-ChildItem -LiteralPath $DevExtract -Filter 'libmpv-2.dll' -File -Recurse)
$MpvExeFiles = @(Get-ChildItem -LiteralPath $MpvExtract -Filter 'mpv.exe' -File -Recurse)
if ($LibMpvFiles.Count -ne 1) { throw "Expected exactly one libmpv-2.dll; found $($LibMpvFiles.Count)." }
if ($MpvExeFiles.Count -ne 1) { throw "Expected exactly one standalone mpv.exe; found $($MpvExeFiles.Count)." }

$DllFolder = Join-Path $ProjectRoot 'dll'
$MpvFolder = Join-Path $ProjectRoot 'mpv-exe'
Clear-Folder $DllFolder
Clear-Folder $MpvFolder
Copy-Item -LiteralPath $LibMpvFiles[0].FullName -Destination (Join-Path $DllFolder 'libmpv-2.dll') -Force
Copy-Item -LiteralPath $MpvExeFiles[0].FullName -Destination (Join-Path $MpvFolder 'mpv.exe') -Force

# 3. Official Gyan.dev FFmpeg 9.0.2 release essentials package.
$FfmpegUrl = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.7z'
$FfmpegArchive = Join-Path $DownloadRoot 'ffmpeg-release-essentials.7z'
$FfmpegHashTextPath = Join-Path $DownloadRoot 'ffmpeg-release-essentials.7z.sha256'
Download-File $FfmpegUrl $FfmpegArchive
Invoke-WebRequest -Uri ($FfmpegUrl + '.sha256') -OutFile $FfmpegHashTextPath -Headers @{ 'User-Agent' = 'AnimePlayer-CPU-safe-build' }
$FfmpegHashText = Get-Content -LiteralPath $FfmpegHashTextPath -Raw
$FfmpegHashMatch = [regex]::Match($FfmpegHashText, '(?i)\b[0-9a-f]{64}\b')
if (-not $FfmpegHashMatch.Success) { throw 'Could not read the official FFmpeg SHA-256 checksum.' }
$FfmpegHash = Verify-Sha256 $FfmpegArchive $FfmpegHashMatch.Value 'FFmpeg 9.0.2 essentials'

$FfmpegExtract = Join-Path $DownloadRoot 'ffmpeg-extracted'
Clear-Folder $FfmpegExtract
& $SevenZip x $FfmpegArchive "-o$FfmpegExtract" -y
if ($LASTEXITCODE -ne 0) { throw 'Could not extract the FFmpeg essentials package.' }
$FfmpegExeFiles = @(Get-ChildItem -LiteralPath $FfmpegExtract -Filter 'ffmpeg.exe' -File -Recurse)
$FfprobeExeFiles = @(Get-ChildItem -LiteralPath $FfmpegExtract -Filter 'ffprobe.exe' -File -Recurse)
if ($FfmpegExeFiles.Count -ne 1 -or $FfprobeExeFiles.Count -ne 1) {
    throw "Expected one ffmpeg.exe and one ffprobe.exe in FFmpeg package; found $($FfmpegExeFiles.Count) and $($FfprobeExeFiles.Count)."
}
$FfmpegRoot = $FfmpegExeFiles[0].Directory.Parent.FullName
$ExpectedFfmpegRoot = Join-Path $ProjectRoot 'ffmpeg-9.0.2-essentials_build'
if (Test-Path -LiteralPath $ExpectedFfmpegRoot) {
    Remove-Item -LiteralPath $ExpectedFfmpegRoot -Recurse -Force
}
Copy-Item -LiteralPath $FfmpegRoot -Destination $ExpectedFfmpegRoot -Recurse -Force

# 4. Anime-specific mpv configuration, scripts, shaders and fonts.
$AnimeRepo = 'https://github.com/Chinna95P/mpv-anime-build.git'
$AnimeFolder = Join-Path $DownloadRoot 'mpv-anime-build'
if (Test-Path -LiteralPath $AnimeFolder) { Remove-Item -LiteralPath $AnimeFolder -Recurse -Force }
& git.exe clone --depth 1 $AnimeRepo $AnimeFolder
if ($LASTEXITCODE -ne 0) { throw 'Could not clone Chinna95P/mpv-anime-build.' }
$AnimeCommit = (& git.exe -C $AnimeFolder rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or -not $AnimeCommit) { throw 'Could not determine the mpv-anime-build revision.' }

$AnimeTarget = Join-Path $ProjectRoot 'mpv-anime-build'
Clear-Folder $AnimeTarget
Get-ChildItem -LiteralPath $AnimeFolder -Force |
    Where-Object { $_.Name -ne '.git' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $AnimeTarget -Recurse -Force }

# Final structure checks stop the build before compiling if any runtime dependency is incomplete.
$Required = @(
    (Join-Path $DllFolder 'libmpv-2.dll'),
    (Join-Path $MpvFolder 'mpv.exe'),
    (Join-Path $ExpectedFfmpegRoot 'bin\ffmpeg.exe'),
    (Join-Path $ExpectedFfmpegRoot 'bin\ffprobe.exe'),
    (Join-Path $AnimeTarget 'mpv.conf'),
    (Join-Path $AnimeTarget 'input.conf'),
    (Join-Path $AnimeTarget 'scripts\uosc\main.lua')
)
foreach ($Path in $Required) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required dependency file is missing after extraction: $Path"
    }
}
foreach ($Directory in @('scripts', 'script-opts', 'shaders', 'fonts')) {
    if (-not (Test-Path -LiteralPath (Join-Path $AnimeTarget $Directory) -PathType Container)) {
        throw "mpv-anime-build is missing required directory: $Directory"
    }
}

$Manifest = @(
    'AnimePlayer dependency manifest',
    "Shinchiro release tag: $($Release.tag_name)",
    "libmpv: $($DevAsset.name) | SHA256 $DevHash",
    "standalone mpv: $($MpvAsset.name) | SHA256 $MpvHash",
    "FFmpeg: 9.0.2 release essentials | SHA256 $FfmpegHash",
    "mpv-anime-build: $AnimeCommit",
    'CPU policy: generic x86-64 mpv packages only; x86_64-v3 packages are rejected.'
)
$ManifestPath = Join-Path $DownloadRoot 'dependency-manifest.txt'
$Manifest | Set-Content -LiteralPath $ManifestPath -Encoding UTF8

Write-Host ''
Write-Host 'All four dependencies downloaded, checksum-verified, extracted and placed.'
Write-Host 'The mpv binaries are generic x86-64 packages, not x86_64-v3.'
Write-Host "Dependency manifest: $ManifestPath"
