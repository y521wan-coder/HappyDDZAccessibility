using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyDDZ.Assistant;

internal sealed class AppSettings
{
    public string OcrBackend { get; set; } = "cpu";
    public bool KeepScreenshots { get; set; }
    public bool AutoLoginWithQq { get; set; } = true;
    public bool SpeakActionResults { get; set; } = true;
    [JsonIgnore] public string? LoadWarning { get; private set; }

    private static string FilePath => Path.Combine(ProjectPaths.Root, "config", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            if (value.OcrBackend is not ("cpu" or "dml"))
            {
                value.OcrBackend = "cpu";
                value.LoadWarning = "OCR 后端设置无效，已恢复为 CPU。";
            }
            return value;
        }
        catch
        {
            return new AppSettings { LoadWarning = "设置文件损坏或无法读取，已使用默认值。可在设置页重新保存。" };
        }
    }

    public void Save()
    {
        var folder = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
            File.Move(temp, FilePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
