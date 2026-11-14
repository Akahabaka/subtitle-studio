using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace SubtitleStudio;

public static class SubtitleInspector
{
    public static HashSet<string> ReferencedFonts(string text)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var columns = Array.Empty<string>(); bool styles = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) styles = line.Equals("[V4+ Styles]", StringComparison.OrdinalIgnoreCase) || line.Equals("[V4 Styles]", StringComparison.OrdinalIgnoreCase);
            if (styles && line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase)) columns = line[7..].Split(',').Select(x => x.Trim()).ToArray();
            if (styles && line.StartsWith("Style:", StringComparison.OrdinalIgnoreCase))
            {
                var fields = line[6..].Split(',');
                int index = Array.FindIndex(columns, c => c.Equals("Fontname", StringComparison.OrdinalIgnoreCase));
                if (index >= 0 && fields.Length > index) required.Add(fields[index].Trim().TrimStart('@'));
            }
        }
        foreach (Match match in Regex.Matches(text, @"\\fn([^\\}]+)")) required.Add(match.Groups[1].Value.Trim().TrimStart('@'));
        required.Remove(""); return required;
    }

    public static async Task<string> Inspect(MediaEngine engine, MediaInfo? media, object source, IReadOnlyList<string> addedFonts, CancellationToken token)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "SubtitleStudio-inspect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            string text;
            if (source is SubtitleSource external) text = MediaEngine.ReadSubtitle(external);
            else if (source is MediaTrack track && media != null && track.Type == "subtitle")
            {
                if (track.Codec is not ("ass" or "ssa" or "subrip" or "mov_text")) throw new IOException("该字幕不是支持文字预览的 ASS / SRT 格式。");
                var path = Path.Combine(scratch, track.Codec is "ass" or "ssa" ? "script.ass" : "script.srt");
                await engine.Run("ffmpeg", ["-nostdin", "-v", "error", "-i", media.Path, "-map", "0:" + track.Index, "-c:s", track.Codec == "mov_text" ? "srt" : "copy", path], token);
                text = MediaEngine.ReadSubtitle(new() { Path = path });
            }
            else throw new IOException("请选中一份外部字幕、已有字幕轨道，或下方烧录字幕。");

            var required = ReferencedFonts(text);
            var system = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var attached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var notes = new List<string>();
            foreach (var family in Fonts.SystemFontFamilies) foreach (var name in family.FamilyNames.Values) system.Add(name);
            void ReadFont(string path, string label)
            {
                try { var font = new GlyphTypeface(new Uri(Path.GetFullPath(path))); foreach (var name in font.FamilyNames.Values) attached.Add(name); foreach (var name in font.Win32FamilyNames.Values) attached.Add(name); }
                catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { notes.Add("无法读取字体 " + label + "：" + ex.Message); }
            }
            foreach (var font in addedFonts) ReadFont(font, Path.GetFileName(font));
            if (media != null && Path.GetExtension(media.Path).Equals(".mkv", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(await engine.Run("mkvmerge", ["-J", media.Path], token));
                foreach (var a in doc.RootElement.GetProperty("attachments").EnumerateArray())
                {
                    var name = a.GetProperty("file_name").GetString() ?? "";
                    var ext = Path.GetExtension(name).ToLowerInvariant();
                    if (ext is not (".ttf" or ".otf")) continue;
                    var id = a.GetProperty("id").GetInt32();
                    var path = Path.Combine(scratch, "font-" + id + ext);
                    await engine.Run("mkvextract", [media.Path, "attachments", id + ":" + path], token);
                    ReadFont(path, name);
                }
            }
            var missing = required.Where(f => !system.Contains(f) && !attached.Contains(f)).ToArray();
            var systemOnly = required.Where(f => system.Contains(f) && !attached.Contains(f)).ToArray();
            return "字幕文字预览（请核对是否乱码）\n" +
                (required.Count == 0 ? "未发现 ASS 字体引用。\n" : "引用字体：" + string.Join("、", required) + "\n" +
                (missing.Length == 0 ? "引用字体均可找到。" : "缺失字体：" + string.Join("、", missing)) + "\n" +
                (systemOnly.Length == 0 ? "" : "仅在本机可用，分享 MKV 时建议添加字体文件：" + string.Join("、", systemOnly) + "\n")) +
                string.Join("\n", notes) + "\n\n" + text;
        }
        finally { try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
