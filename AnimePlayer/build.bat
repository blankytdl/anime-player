@echo off
setlocal enabledelayedexpansion
title AnimePlayer + AnimeLibrary build
cd /d "%~dp0"

rem ======================= OPTIONS =======================
rem CONFIG_MODE: APPDATA  = install the config to %%APPDATA%%\mpv (what the mpv-anime-build readme says; existing folder is backed up first)
rem              PORTABLE = put it in dist\portable_config next to mpvnet.exe
if not defined CONFIG_MODE set CONFIG_MODE=APPDATA
rem POTPLAYER_WINS: 1 = PotPlayer's own keys win on a clash (the build's displaced action is moved or disabled), 0 = the anime build's keys win.
rem                  Every decision is listed in key-conflicts.txt in the config folder.
set POTPLAYER_WINS=1
set FFMPEG_DIR=ffmpeg-9.0.2-essentials_build
rem CLEAN_INSTALL: 1 = after the backup, files in the config folder that are NOT part of the new config are deleted (old clashing
rem                  scripts, leftovers of earlier builds). resume.json, queue.json, queue-list.json, uosc_theme.json (the saved theme), watched-files.json, ap-volume.conf, watched.log, anime-settings.json, uosc_history.json,
rem                  watch_later and cache are always kept. 0 = only add / update files.
set CLEAN_INSTALL=1
rem USE_BUILD_FOLDER: 1 = when mpv-anime-build\ is present, install a fresh copy of IT (plus the AnimePlayer additions) instead of
rem                  patching what is installed now. 0 = patch the installed config.
set USE_BUILD_FOLDER=1
rem MPV_EXE_DIR: folder holding a STANDALONE mpv.exe (shinchiro mpv-x86_64, not -v3). Needed only for the seek-bar preview (thumbfast).
set MPV_EXE_DIR=mpv-exe
rem NUGET_SOURCE: optional mirror if nuget.org is slow/blocked for you, e.g. https://nuget.cdn.azure.cn/v3/index.json  (leave empty for the default)
set NUGET_SOURCE=
rem =======================================================

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET SDK not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download then run build.bat again.
  if not defined CI pause & exit /b 1
)
for /f "delims=" %%v in ('dotnet --version') do set SDKV=%%v
echo Using .NET SDK %SDKV%
echo %SDKV% | findstr /b "10." >nul
if errorlevel 1 (
  echo [ERROR] The player targets net10.0 and needs the .NET 10 SDK, found %SDKV%.
  if not defined CI pause & exit /b 1
)

if exist dist rmdir /s /q dist
mkdir dist

rem a half-downloaded runtime pack ("Central Directory corrupt") from an earlier failed build - this project no longer needs it
if exist "%USERPROFILE%\.nuget\packages\microsoft.netcore.app.runtime.win-x64" rmdir /s /q "%USERPROFILE%\.nuget\packages\microsoft.netcore.app.runtime.win-x64"

echo.
echo ===== [1/3] Player - mpvnet.exe =====
call :restore "src\MpvNet.Windows\MpvNet.Windows.csproj"
if errorlevel 1 ( echo [ERROR] Package restore failed - check your internet connection or set NUGET_SOURCE at the top of build.bat. & if not defined CI pause & exit /b 1 )
set APBUILD=0
if exist "BUILD.txt" set /p APBUILD=<"BUILD.txt"
echo AnimePlayer build !APBUILD!
dotnet publish "src\MpvNet.Windows\MpvNet.Windows.csproj" -c Release --no-restore -o "dist" -p:FileVersion=7.1.2.!APBUILD! -p:AssemblyVersion=7.1.2.!APBUILD! -p:InformationalVersion=7.1.2.!APBUILD!
if errorlevel 1 ( echo [ERROR] Player build failed. Copy the errors above and send them to me. & if not defined CI pause & exit /b 1 )

echo.
echo ===== [2/3] Library - AnimeLibrary.exe =====
call :restore "AnimeLibrary\AnimeLibrary.csproj"
if errorlevel 1 ( echo [ERROR] Package restore failed. & if not defined CI pause & exit /b 1 )
dotnet publish "AnimeLibrary\AnimeLibrary.csproj" -c Release --no-restore -o "dist"
if errorlevel 1 ( echo [ERROR] Library build failed. Copy the errors above and send them to me. & if not defined CI pause & exit /b 1 )

echo.
echo ===== [3/3] Runtime files and config =====
if exist "BUILD.txt" copy /y "BUILD.txt" "dist\BUILD.txt" >nul
set "ROOT=%~dp0"

rem ---- 1) libmpv + ffmpeg + preview mpv.exe: copied FIRST, so a problem with the config below can never leave dist\ without them ----
set HAVE_DLL=0
for /r "dll" %%f in (*.dll) do copy /y "%%f" "dist\" >nul
if exist "dist\libmpv-2.dll" set HAVE_DLL=1

set "FF="
set "FP="
for /r "%FFMPEG_DIR%" %%f in (ffmpeg.exe) do if exist "%%f" if not defined FF set "FF=%%f"
for /r "%FFMPEG_DIR%" %%f in (ffprobe.exe) do if exist "%%f" if not defined FP set "FP=%%f"
if defined FF copy /y "!FF!" "dist\" >nul
if defined FP copy /y "!FP!" "dist\" >nul

set "MPVEXE_SRC="
set "MPVEXE_SRC_DIR="
for /r "%MPV_EXE_DIR%" %%f in (mpv.exe) do if exist "%%f" if not defined MPVEXE_SRC (
  set "MPVEXE_SRC=%%f"
  set "MPVEXE_SRC_DIR=%%~dpf"
)
set "MPVEXE_DEST="
if defined MPVEXE_SRC (
  mkdir "dist\mpv-thumb" 2>nul
  rem thumbfast starts mpv.exe as a separate process. Copy its whole runtime folder,
  rem not only the EXE, so its adjacent DLLs remain available after moving the player.
  robocopy "!MPVEXE_SRC_DIR!" "dist\mpv-thumb" /E /XF PUT_MPV_EXE_HERE.txt /NFL /NDL /NJH /NJS /NP >nul
  if errorlevel 8 (
    echo [WARN] Could not copy the complete standalone mpv runtime into dist\mpv-thumb.
    set "MPVEXE_SRC="
  )
  if exist "dist\mpv-thumb\mpv.exe" set "MPVEXE_DEST=!ROOT!dist\mpv-thumb\mpv.exe"
)

rem ---- 2) config (a failure here is reported but never aborts the build) ----
set "CFG_STATUS=failed"
call :install_config

echo.
set MISSING=0
if "!HAVE_DLL!"=="0" ( echo [WARN] dist\libmpv-2.dll is MISSING - the player cannot start. Put libmpv-2.dll from the shinchiro mpv-dev archive into the dll folder, see dll\PUT_libmpv_HERE.txt & set MISSING=1 )
if not defined MPVEXE_SRC ( echo [WARN] No mpv.exe in %MPV_EXE_DIR% - the seek-bar preview stays blank, see %MPV_EXE_DIR%\PUT_MPV_EXE_HERE.txt & set MISSING=1 )
if not defined FF ( echo [WARN] ffmpeg.exe not found in %FFMPEG_DIR% - the subtitle sync window needs it, see the PUT_FFMPEG_HERE.txt there & set MISSING=1 )
if not "!CFG_STATUS!"=="ok" ( echo [WARN] The config was NOT installed - read dist\config-merge.log and the messages above. & set MISSING=1 )
echo.
echo Done - AnimePlayer build !APBUILD!. Output folder: %~dp0dist   - mpvnet.exe and AnimeLibrary.exe
if "!MISSING!"=="1" echo Fix the [WARN] lines above and run build.bat again.
if not defined CI pause
exit /b 0

rem =====================================================================================================
rem  Installs / repairs / extends the mpv config. Order of preference for the BASE config:
rem    INSTALLED = the config that is already in %%APPDATA%%\mpv (the anime build, or any other mpv config)
rem    FOLDER    = fresh install from the mpv-anime-build folder (nested extracts are found)
rem    BASIC     = no config anywhere yet: the small built-in config
rem  The merge script repairs files that an earlier AnimePlayer build replaced (mpv.conf / input.conf) from your own backup.
rem =====================================================================================================
:install_config
set "STAGE=%~dp0dist\_cfg"
if /i "%CONFIG_MODE%"=="PORTABLE" (set "TARGET=!ROOT!dist\portable_config") else (set "TARGET=%APPDATA%\mpv")

set "BUILD_SRC="
if exist "mpv-anime-build\mpv.conf" set "BUILD_SRC=!ROOT!mpv-anime-build"
if not defined BUILD_SRC for /f "delims=" %%f in ('dir /s /b "mpv-anime-build\mpv.conf" 2^>nul') do if not defined BUILD_SRC set "BUILD_SRC=%%~dpf"
if defined BUILD_SRC if "!BUILD_SRC:~-1!"=="\" set "BUILD_SRC=!BUILD_SRC:~0,-1!"

set "MODE=BASIC"
if exist "!TARGET!\scripts\uosc\main.lua" set "MODE=INSTALLED"
if "%USE_BUILD_FOLDER%"=="1" if defined BUILD_SRC set "MODE=FOLDER"
if "!MODE!"=="BASIC" if defined BUILD_SRC set "MODE=FOLDER"
if "!MODE!"=="BASIC" if exist "!TARGET!\mpv.conf" set "MODE=INSTALLED"
echo Config mode: !MODE!   target: !TARGET!
if defined BUILD_SRC echo Anime build folder: !BUILD_SRC!

if exist "!STAGE!" rmdir /s /q "!STAGE!"
if "!MODE!"=="INSTALLED" robocopy "!TARGET!" "!STAGE!" /E /XD cache watch_later /NFL /NDL /NJH /NJS /NP >nul
if "!MODE!"=="FOLDER" robocopy "!BUILD_SRC!" "!STAGE!" /E /XD .git .github docs screenshots /XF Readme.md CHANGELOG.md FAQ.md AGENTS.md index.html sitemap.xml favicon.svg .gitignore .gitmodules .nojekyll PUT_ANIME_BUILD_HERE.txt /NFL /NDL /NJH /NJS /NP >nul
if "!MODE!"=="BASIC" robocopy "portable_config" "!STAGE!" /E /NFL /NDL /NJH /NJS /NP >nul

if "!MODE!"=="BASIC" (
  echo [WARN] No existing config and no mpv-anime-build folder - installing the basic built-in config.
  goto :deploy
)

rem add ONLY the new files - never my mpv.conf / input.conf / overrides.conf
if not exist "!STAGE!\scripts" mkdir "!STAGE!\scripts"
if not exist "!STAGE!\script-opts" mkdir "!STAGE!\script-opts"
for %%f in ("portable_config\scripts\ap-*.lua" "portable_config\scripts\00-thumbfast-path.lua" "portable_config\scripts\midclick-jump.lua" "portable_config\scripts\speed-osd.lua") do copy /y "%%~f" "!STAGE!\scripts\" >nul
if not exist "!STAGE!\script-opts\midclick-jump.conf" copy /y "portable_config\script-opts\midclick-jump.conf" "!STAGE!\script-opts\" >nul
if not exist "!STAGE!\script-opts\ap-resume.conf" copy /y "portable_config\script-opts\ap-resume.conf" "!STAGE!\script-opts\" >nul
if not exist "!STAGE!\script-opts\ap-smooth.conf" copy /y "portable_config\script-opts\ap-smooth.conf" "!STAGE!\script-opts\" >nul
if not exist "!STAGE!\script-opts\ap-queue.conf" copy /y "portable_config\script-opts\ap-queue.conf" "!STAGE!\script-opts\" >nul
copy /y "portable_config\input.conf" "!STAGE!\animeplayer-input.conf" >nul

powershell -NoProfile -ExecutionPolicy Bypass -File "tools\merge-config.ps1" -Cfg "!STAGE!" -PatchFile "!ROOT!tools\patches" -PotWins %POTPLAYER_WINS% -MpvExe "!MPVEXE_DEST!" -TargetDir "!TARGET!" -BuildSrc "!BUILD_SRC!" > "!ROOT!dist\config-merge.log" 2>&1
set "PSRC=!errorlevel!"
type "!ROOT!dist\config-merge.log"
if not "!PSRC!"=="0" (
  echo [ERROR] The config merge failed, so NOTHING of your config was changed. The player files are complete.
  if exist "!STAGE!" rmdir /s /q "!STAGE!"
  goto :eof
)

:deploy
rem SAFETY: never install (and never clean) from an incomplete staging folder
if not exist "!STAGE!\mpv.conf" ( echo [ERROR] The staged config is incomplete - nothing was changed. & if exist "!STAGE!" rmdir /s /q "!STAGE!" & goto :eof )
if not exist "!STAGE!\scripts" ( echo [ERROR] The staged config has no scripts folder - nothing was changed. & if exist "!STAGE!" rmdir /s /q "!STAGE!" & goto :eof )
rem back up what is there, then copy (existing files are updated, nothing is deleted)
if exist "!TARGET!\mpv.conf" (
  for /f %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set STAMP=%%t
  echo Backing up the current config to !TARGET!.backup-!STAMP!
  robocopy "!TARGET!" "!TARGET!.backup-!STAMP!" /E /XD cache watch_later /NFL /NDL /NJH /NJS /NP >nul
)
if not exist "!TARGET!" mkdir "!TARGET!"
if "%CLEAN_INSTALL%"=="1" (
  echo Clean install: leftovers that are not part of the new config are being deleted - your data and the backup are kept.
  robocopy "!STAGE!" "!TARGET!" /MIR /XD cache watch_later /XF anime-settings.json resume.json queue.json queue-list.json watched.log uosc_history.json uosc_theme.json watched-files.json durations.json ap-volume.conf /NFL /NDL /NJH /NJS /NP >nul
) else (
  robocopy "!STAGE!" "!TARGET!" /E /XF anime-settings.json resume.json queue.json queue-list.json watched.log uosc_theme.json watched-files.json durations.json ap-volume.conf /NFL /NDL /NJH /NJS /NP >nul
)
for %%f in (anime-settings.json resume.json queue.json queue-list.json watched.log uosc_theme.json watched-files.json durations.json ap-volume.conf) do (
  if not exist "!TARGET!\%%f" if exist "!STAGE!\%%f" copy /y "!STAGE!\%%f" "!TARGET!\%%f" >nul
)
echo Config installed to !TARGET!
if exist "!STAGE!" rmdir /s /q "!STAGE!"
set "CFG_STATUS=ok"
echo.
echo ----- verifying the installed config -----
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\verify-config.ps1" -Cfg "!TARGET!"
goto :eof

:restore
set TRY=0
:restore_try
set /a TRY+=1
echo Restoring packages for %~1 - attempt !TRY! of 4
if defined NUGET_SOURCE ( dotnet restore "%~1" --source "%NUGET_SOURCE%" ) else ( dotnet restore "%~1" )
if not errorlevel 1 exit /b 0
if !TRY! GEQ 4 exit /b 1
echo Restore failed - waiting 5 seconds and retrying...
timeout /t 5 /nobreak >nul
goto restore_try

