using System.IO;
using System.Text.Json;

namespace LogitechBatteryWidget;

/// <summary>위젯 위치·항상 위 설정. %APPDATA%\LogitechBatteryWidget\config.json 에 저장.</summary>
public sealed class WidgetConfig
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LogitechBatteryWidget", "config.json");

    public double X { get; set; } = 40;
    public double Y { get; set; } = 40;
    public bool Topmost { get; set; } = true;

    public static WidgetConfig Load()
    {
        try { return JsonSerializer.Deserialize<WidgetConfig>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { /* 저장 실패는 무시 */ }
    }
}
