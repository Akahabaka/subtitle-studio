using System.IO;
using System.Text.Json;

namespace SubtitleStudio;

public sealed record UserPreferences
{
    public int Mode { get; init; }
    public int Container { get; init; }
    public string Destination { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    public string Font { get; init; } = "Microsoft YaHei";
    public string FontSize { get; init; } = "24";
    public int Alignment { get; init; }
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SubtitleStudio", "settings.json");
    public static UserPreferences Load(string path)
    {
        try { return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, true);
    }
}
