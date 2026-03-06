using System;
using System.IO;
using System.Text.Json;

namespace ModernUOConfigurator;

internal static class AppSettings
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModernUOConfigurator",
        "settings.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public static string? LoadRootPath()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var json = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(_path));
            return string.IsNullOrEmpty(json?.RootPath) ? null : json.RootPath;
        }
        catch { return null; }
    }

    public static void SaveRootPath(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new SettingsData { RootPath = path }, _opts));
        }
        catch { }
    }

    private sealed class SettingsData
    {
        public string? RootPath { get; set; }
    }
}
