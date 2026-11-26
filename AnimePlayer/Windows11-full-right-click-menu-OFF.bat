@echo off
rem Back to the compact Windows 11 right-click menu.
reg delete "HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}" /f
taskkill /f /im explorer.exe >nul 2>&1
start explorer.exe
echo Done. Right-click shows the compact menu again.
pause
