using System.Windows;
namespace AnimeLibrary;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        try { app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Theme.xaml", UriKind.Absolute) }); }
        catch (Exception ex) { MessageBox.Show("The theme could not be loaded, the default look is used.\n\n" + ex.Message, "Anime Library"); }
        app.DispatcherUnhandledException += (_, e) =>
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Store.Dir, "crash.log"), DateTime.Now + "\n" + e.Exception + "\n\n"); } catch { }
            MessageBox.Show("Something went wrong but the library is still running:\n\n" + e.Exception.Message + "\n\n(details: crash.log in %APPDATA%\\AnimeLibrary)", "Anime Library");
            e.Handled = true;
        };
        app.Run(new MainWindow());
    }
}
