using System.IO;
using System.Text.Json;

namespace SensorDeck.Services;

public sealed class PanelEntry
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class AppSettings
{
    public List<PanelEntry> Panel { get; set; } = new();
    public double OverlayLeft { get; set; } = 40;
    public double OverlayTop { get; set; } = 40;
    public double OverlayFontSize { get; set; } = 15;
    public double BackgroundOpacity { get; set; } = 0.75;
    public bool ClickThrough { get; set; }
    public bool OverlayVisible { get; set; } = true;
    /// <summary>Win32 MOD_* flags (Alt=1, Ctrl=2, Shift=4, Win=8).</summary>
    public uint HotkeyModifiers { get; set; } = 0x2 | 0x1; // Ctrl+Alt
    /// <summary>Win32 virtual key code.</summary>
    public uint HotkeyKey { get; set; } = 0x48; // H
    public int UpdateIntervalMs { get; set; } = 1000;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path { get; }

    public SettingsService(string? overridePath = null)
    {
        Path = overridePath ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SensorDeck", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt file: start with defaults rather than crash.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(settings, Json));
        }
        catch
        {
            // Not fatal; settings will just not persist this time.
        }
    }
}
