
using Microsoft.Win32;

using MpvNet.Windows.Help;

namespace MpvNet.Windows;

public static class FileAssociation
{
    public static void Register(string perceivedType, string[] extensions)
    {
        string exePath = Environment.ProcessPath!;
        string exeFilename = Path.GetFileName(exePath);
        string exeFilenameNoExt = Path.GetFileNameWithoutExtension(exePath);

        string[] protocols = ["ytdl", "rtsp", "srt", "srtp"];

        if (perceivedType != "unreg")
        {
            foreach (string it in protocols)
            {
                RegistryHelp.SetValue($@"HKCR\{it}", $"{it.ToUpper()} Protocol", "");
                RegistryHelp.SetValue($@"HKCR\{it}\shell\open\command", "", $"\"{exePath}\" \"%1\"");
            }

            RegistryHelp.SetValue(@"HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\" + exeFilename, "", exePath);
            RegistryHelp.SetValue(@"HKCR\Applications\" + exeFilename, "FriendlyAppName", "mpv.net media player");
            // (build 33) Open / Open with / Enter on SEVERAL selected files = ONE start with all of them (no limit of 15 windows, works in the compact Windows 11 menu too)
            RegistryHelp.SetValue(@"HKCR\Applications\" + exeFilename + @"\shell\open", "MultiSelectModel", "Player");
            RegistryHelp.SetValue(@"HKCR\Applications\" + exeFilename + @"\shell\open\command", "", $"\"{exePath}\" \"%1\"");
            RegistryHelp.SetValue(@"HKCR\SystemFileAssociations\video\OpenWithList\" + exeFilename, "", "");
            RegistryHelp.SetValue(@"HKCR\SystemFileAssociations\audio\OpenWithList\" + exeFilename, "", "");
            // (build 31) right-click on SEVERAL selected videos: one entry plays them all, one adds them all to the end of the playlist. MultiSelectModel=Player makes
            // Explorer start the player ONCE with every selected file (otherwise it starts one process per file). They are in the classic menu
            // (Windows 11: "Show more options" / Shift+F10).
            foreach (string kind in new[] { "video", "audio" })
            {
                string verbs = @"HKCR\SystemFileAssociations\" + kind + @"\shell\";
                RegistryHelp.SetValue(verbs + "AnimePlayer.PlayAll", "", "Play all with AnimePlayer");
                RegistryHelp.SetValue(verbs + "AnimePlayer.PlayAll", "Icon", exePath);
                RegistryHelp.SetValue(verbs + "AnimePlayer.PlayAll", "MultiSelectModel", "Player");
                RegistryHelp.SetValue(verbs + @"AnimePlayer.PlayAll\command", "", $"\"{exePath}\" \"%1\"");
                RegistryHelp.SetValue(verbs + "AnimePlayer.Queue", "", "Add all to AnimePlayer playlist");
                RegistryHelp.SetValue(verbs + "AnimePlayer.Queue", "Icon", exePath);
                RegistryHelp.SetValue(verbs + "AnimePlayer.Queue", "MultiSelectModel", "Player");
                RegistryHelp.SetValue(verbs + @"AnimePlayer.Queue\command", "", $"\"{exePath}\" --queue \"%1\"");
            }

            RegistryHelp.SetValue(@"HKLM\SOFTWARE\RegisteredApplications", "mpv.net", @"SOFTWARE\Clients\Media\mpv.net\Capabilities");
            RegistryHelp.SetValue(@"HKLM\SOFTWARE\Clients\Media\mpv.net\Capabilities", "ApplicationDescription", "mpv.net media player");
            RegistryHelp.SetValue(@"HKLM\SOFTWARE\Clients\Media\mpv.net\Capabilities", "ApplicationName", "mpv.net");

            foreach (string ext in extensions)
            {
                RegistryHelp.SetValue(@"HKCR\Applications\" + exeFilename + @"\SupportedTypes", "." + ext, "");
                RegistryHelp.SetValue(@"HKCR\" + "." + ext, "", exeFilenameNoExt + "." + ext);
                RegistryHelp.SetValue(@"HKCR\" + "." + ext + @"\OpenWithProgIDs", exeFilenameNoExt + "." + ext, "");
                RegistryHelp.SetValue(@"HKCR\" + "." + ext, "PerceivedType", perceivedType);
                RegistryHelp.SetValue(@"HKCR\" + exeFilenameNoExt + "." + ext + @"\shell\open", "MultiSelectModel", "Player");
                RegistryHelp.SetValue(@"HKCR\" + exeFilenameNoExt + "." + ext + @"\shell\open\command", "", $"\"{exePath}\" \"%1\"");
                RegistryHelp.SetValue(@"HKLM\SOFTWARE\Clients\Media\mpv.net\Capabilities\FileAssociations", "." + ext, exeFilenameNoExt + "." + ext);
            }
        }
        else
        {
            foreach (string i in protocols)
                RegistryHelp.RemoveKey($@"HKCR\{i}");

            RegistryHelp.RemoveKey(@"HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\" + exeFilename);
            RegistryHelp.RemoveKey(@"HKCR\Applications\" + exeFilename);
            RegistryHelp.RemoveKey(@"HKLM\SOFTWARE\Clients\Media\mpv.net");
            foreach (string kind in new[] { "video", "audio" })
            {
                RegistryHelp.RemoveKey(@"HKCR\SystemFileAssociations\" + kind + @"\shell\AnimePlayer.PlayAll");
                RegistryHelp.RemoveKey(@"HKCR\SystemFileAssociations\" + kind + @"\shell\AnimePlayer.Queue");
            }
            RegistryHelp.RemoveKey(@"HKCR\SystemFileAssociations\video\OpenWithList\" + exeFilename);
            RegistryHelp.RemoveKey(@"HKCR\SystemFileAssociations\audio\OpenWithList\" + exeFilename);

            RegistryHelp.RemoveValue(@"HKLM\SOFTWARE\RegisteredApplications", "mpv.net");

            foreach (string id in Registry.ClassesRoot.GetSubKeyNames())
            {
                if (id.StartsWith(exeFilenameNoExt + "."))
                    Registry.ClassesRoot.DeleteSubKeyTree(id);

                RegistryHelp.RemoveValue($@"HKCR\Software\Classes\{id}\OpenWithProgIDs", exeFilenameNoExt + id);
                RegistryHelp.RemoveValue($@"HKLM\Software\Classes\{id}\OpenWithProgIDs", exeFilenameNoExt + id);
            }
        }
    }
}
