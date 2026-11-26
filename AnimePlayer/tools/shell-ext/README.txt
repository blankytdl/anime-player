AnimePlayer entries in the Windows 11 right-click menu
======================================================
"Play with AnimePlayer" and "Add to AnimePlayer playlist" (with several files selected: "Play all ..." / "Add all ...") in the FIRST menu,
next to File Locksmith / PowerRename / WinRAR - no "Show more options" needed.

Install:    double-click Register-ContextMenu.bat  (say Yes to the administrator question; it needs that once to trust the certificate)
Uninstall:  Unregister-ContextMenu.bat
Run Register again after you move the player folder or put a newer build in.

How it works: Windows 11 only loads shell extensions of programs that have a package identity. This folder holds a tiny signed "sparse" package
(AnimePlayerShell.msix, identity only) + AnimePlayerShell.dll (next to mpvnet.exe) that implements the two commands. The certificate is made fresh
for every build; Register-ContextMenu.ps1 replaces the old one. Nothing outside the player folder is changed except the package registration
and the certificate in "Trusted People".

If something fails, register.log in this folder says why. The classic menu entries (Shift+F10) from Setup > Register video file associations stay as they were.
