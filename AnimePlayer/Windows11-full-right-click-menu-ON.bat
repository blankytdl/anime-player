@echo off
rem Windows 11: always show the FULL (classic) right-click menu, so "Play all with AnimePlayer" / "Add all to AnimePlayer queue" are in the first menu.
rem Only changes the current user. Undo with Windows11-full-right-click-menu-OFF.bat. Explorer restarts (the taskbar flashes once).
reg add "HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32" /ve /d "" /f
taskkill /f /im explorer.exe >nul 2>&1
start explorer.exe
echo Done. Right-click now shows the full menu.
pause
