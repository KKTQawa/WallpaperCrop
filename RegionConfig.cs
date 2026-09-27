using System.Text.Json;

namespace WallpaperCrop;

public sealed class AppConfig
{
    public List<SavedRegion> Regions { get; set; } = [];
    public bool StartWithWindows { get; set; }
}

internal static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WallpaperCrop";

    public static void SetEnabled(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。");
            key.SetValue(ValueName, $"\"{executable}\"");
        }
        else key.DeleteValue(ValueName, false);
    }
}

public sealed class SavedRegion
{
    public string Name { get; set; } = "参考区域";
    public int Slot { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public int DisplayWidth { get; set; }
    public int DisplayHeight { get; set; }
}

internal static class ConfigStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WallpaperCrop");
    private static readonly string FilePath = Path.Combine(Folder, "config.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options) ?? new AppConfig(); }
        catch { return new AppConfig(); }
    }
    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, Options));
    }
}
