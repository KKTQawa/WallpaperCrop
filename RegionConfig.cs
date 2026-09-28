using System.Text.Json;
using System.Drawing.Imaging;

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
    // File name of the frozen image captured when this preset was saved.
    // Older configurations do not contain this value and remain readable.
    public string? CaptureFile { get; set; }
}

internal static class ConfigStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WallpaperCrop");
    private static readonly string FilePath = Path.Combine(Folder, "config.json");
    private static readonly string CaptureFolder = Path.Combine(Folder, "captures");
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
    public static string SaveCapture(int slot, Image image)
    {
        Directory.CreateDirectory(CaptureFolder);
        var fileName = $"slot-{slot}.png";
        image.Save(Path.Combine(CaptureFolder, fileName), ImageFormat.Png);
        return fileName;
    }
    public static Bitmap? LoadCapture(SavedRegion region)
    {
        if (string.IsNullOrWhiteSpace(region.CaptureFile)) return null;
        var fileName = Path.GetFileName(region.CaptureFile);
        var path = Path.Combine(CaptureFolder, fileName);
        if (!File.Exists(path)) return null;
        try
        {
            using var source = new Bitmap(path);
            return new Bitmap(source);
        }
        catch (Exception) { return null; }
    }
    public static void DeleteCaptures()
    {
        if (Directory.Exists(CaptureFolder)) Directory.Delete(CaptureFolder, true);
    }
}
