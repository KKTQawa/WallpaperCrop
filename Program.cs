namespace WallpaperPeek;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var singleInstance = new Mutex(true, @"Local\WallpaperPeek.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
