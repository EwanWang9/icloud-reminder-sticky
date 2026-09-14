using System.Text.Json;
using System.IO;

namespace ICloudReminderSticky;

internal sealed class AppSettings
{
    public double Width { get; set; } = 430;
    public double Height { get; set; } = 720;
    public bool Topmost { get; set; } = true;
    public double ZoomFactor { get; set; } = 0.85;
    public bool DesktopMode { get; set; } = true;
    public int LastSyncedCount { get; set; }
    public DateTime? LastSyncUtc { get; set; }

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "iCloudReminderSticky");

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public static string BrowserDataFolder => Path.Combine(Folder, "WebView2");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Invalid settings should never prevent the sticky note from starting.
        }

        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
