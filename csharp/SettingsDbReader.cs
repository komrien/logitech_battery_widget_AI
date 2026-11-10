using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LogitechBatteryWidget;

/// <summary>
/// G HUB API에 연결할 수 없을 때 쓰는 대체 경로.
/// %LOCALAPPDATA%\LGHUB\settings.db 를 읽기 전용으로 열어 마지막 저장 잔량을 읽는다(충전 여부 없음).
/// </summary>
public static class SettingsDbReader
{
    static readonly string DbPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LGHUB", "settings.db");

    public static List<(string Name, int Percentage, DateTimeOffset? Time)> Read()
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = DbPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
        using var con = new SqliteConnection(cs);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "select file from data order by _id desc limit 1";
        var blob = cmd.ExecuteScalar() switch
        {
            byte[] b => b,
            string s => System.Text.Encoding.UTF8.GetBytes(s),
            _ => throw new InvalidDataException("settings.db 형식을 알 수 없어요"),
        };

        var result = new List<(string, int, DateTimeOffset?)>();
        using var doc = JsonDocument.Parse(blob);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var parts = prop.Name.Split('/');
            if (parts.Length != 3 || parts[0] != "battery" || parts[2] != "percentage") continue;
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;

            var pct = prop.Value.TryGetProperty("percentage", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
            DateTimeOffset? time = null;
            if (prop.Value.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(t.GetString(), out var parsed))
                time = parsed;
            result.Add((parts[1].ToUpperInvariant(), pct, time));
        }
        return result;
    }
}
