using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SubtitleStudio;

public sealed class MediaTrack
{
    public bool Selected { get; set; } = true;
    public int Index { get; set; }
    public string Type { get; set; } = "";
    public string Codec { get; set; } = "";
    public string Name { get; set; } = "";
    public string Language { get; set; } = "und";
    public bool Default { get; set; }
    public double StartTime { get; set; }
    public double Rotation { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Compatibility { get; set; } = "";
    public override string ToString() => $"#{Index} {Type} · {Codec} · {Name} ({Language})";
}

public sealed record MediaInfo(string Path, double Duration, double Start, bool IsHdr, List<MediaTrack> Tracks);
public sealed class SubtitleSource
{
    public bool Selected { get; set; } = true;
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Language { get; set; } = "zh";
    public bool Default { get; set; }
    public string Encoding { get; set; } = "自动";
    public override string ToString() => Name;
}

public sealed class MediaEngine(string directory)
{
    private readonly StringBuilder log = new();
    public string Log { get { lock (log) return log.ToString(); } }
    public string[] Missing => new[] { "ffmpeg", "ffprobe", "mkvmerge", "mkvextract" }
        .Where(n => !File.Exists(System.IO.Path.Combine(directory, n + ".exe"))).ToArray();

    public async Task<string> Run(string name, IEnumerable<string> arguments, CancellationToken token,
        Action<string>? lineReceived = null, string? workingDirectory = null, bool captureOutput = true)
    {
        var args = arguments.ToArray();
        var info = new ProcessStartInfo(System.IO.Path.Combine(directory, name + ".exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = workingDirectory ?? directory
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        lock (log) log.AppendLine($"[{DateTime.Now:O}] {name} {JsonSerializer.Serialize(args)}");
        using var process = Process.Start(info) ?? throw new IOException("无法启动处理引擎。");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = new StringBuilder();
        var error = new StringBuilder();
        async Task Read(StreamReader reader, StringBuilder target)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (captureOutput || !ReferenceEquals(target, output)) target.AppendLine(line);
                lineReceived?.Invoke(line);
            }
        }
        await Task.WhenAll(Read(process.StandardOutput, output), Read(process.StandardError, error), process.WaitForExitAsync());
        lock (log) log.Append(output).Append(error).AppendLine($"Exit: {process.ExitCode}");
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0 && !(name == "mkvmerge" && process.ExitCode == 1))
            throw new IOException("处理引擎失败。请检查轨道兼容性、文件是否损坏及磁盘空间。\n" +
                (error.Length > 0 ? error.ToString() : output.ToString())[..Math.Min(1500, error.Length > 0 ? error.Length : output.Length)]);
        return output.ToString();
    }

    public async Task<MediaInfo> Probe(string path, CancellationToken token)
    {
        var json = await Run("ffprobe", ["-v", "error", "-show_streams", "-show_format", "-of", "json", path], token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        static string Text(JsonElement e, string key, string fallback = "") => e.TryGetProperty(key, out var p) ? p.ToString() : fallback;
        static double Number(JsonElement e, string key) => double.TryParse(Text(e, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
        var tracks = new List<MediaTrack>();
        bool hdr = false;
        foreach (var s in root.GetProperty("streams").EnumerateArray())
        {
            s.TryGetProperty("tags", out var tags);
            string Tag(string key, string fallback = "") => tags.ValueKind == JsonValueKind.Object ? Text(tags, key, fallback) : fallback;
            var t = new MediaTrack { Index = s.GetProperty("index").GetInt32(), Type = Text(s, "codec_type"), Codec = Text(s, "codec_name"), Name = Tag("title", Tag("filename")), Language = Tag("language", "und") };
            if (s.TryGetProperty("disposition", out var disp)) t.Default = Number(disp, "default") == 1;
            t.StartTime = Number(s, "start_time");
            t.Width = (int)Number(s, "width"); t.Height = (int)Number(s, "height");
            if (s.TryGetProperty("side_data_list", out var sideData))
                foreach (var side in sideData.EnumerateArray())
                    if (side.TryGetProperty("rotation", out _)) t.Rotation = Number(side, "rotation");
            tracks.Add(t);
            if (t.Type == "video") hdr |= Text(s, "color_transfer") is "smpte2084" or "arib-std-b67" || s.ToString().Contains("DOVI", StringComparison.OrdinalIgnoreCase) || s.ToString().Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase);
        }
        var format = root.GetProperty("format");
        var start = Number(format, "start_time");
        var duration = Number(format, "duration");
        // Muxers differ in their treatment of Matroska Duration with nonzero origins.
        // Measure video packet endpoints in that case rather than guessing from the tag.
        if (start != 0 && Text(format, "format_name").Contains("matroska"))
        {
            var packets = await Run("ffprobe", ["-v", "error", "-select_streams", "v:0", "-show_packets", "-show_entries", "packet=pts_time,duration_time", "-of", "csv=p=0", path], token);
            double end = start;
            foreach (var line in packets.Split('\n'))
            {
                var fields = line.Trim().Split(',');
                if (fields.Length >= 2 && double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var pts))
                {
                    double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var packetDuration);
                    end = Math.Max(end, pts + packetDuration);
                }
            }
            if (end > start) duration = end - start;
        }
        return new(path, Math.Max(0, duration), start, hdr, tracks);
    }

    public async Task<Dictionary<int, (double Start, double End)>> TrackTimes(string path, CancellationToken token)
    {
        var ranges = new Dictionary<int, (double Start, double End)>();
        await Run("ffprobe", ["-v", "error", "-show_packets", "-show_entries", "packet=stream_index,pts_time,duration_time", "-of", "compact=p=0:nk=0", path], token, line =>
        {
            var fields = line.Split('|').Select(x => x.Split('=', 2)).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);
            if (!fields.TryGetValue("stream_index", out var indexText) || !int.TryParse(indexText, out var index) ||
                !fields.TryGetValue("pts_time", out var ptsText) || !double.TryParse(ptsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var pts)) return;
            fields.TryGetValue("duration_time", out var durationText);
            double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration);
            var previous = ranges.GetValueOrDefault(index, (pts, pts));
            ranges[index] = (Math.Min(previous.Item1, pts), Math.Max(previous.Item2, pts + duration));
        }, captureOutput: false);
        return ranges;
    }

    public static string Mp4Compatibility(MediaTrack t) => t.Type switch
    {
        "video" when new[] { "h264", "hevc", "av1", "mpeg4", "mpeg2video", "mjpeg", "vp9" }.Contains(t.Codec) => "可直接复制",
        "audio" when new[] { "aac", "mp3", "ac3", "eac3", "alac", "opus", "flac", "dts" }.Contains(t.Codec) => "可复制，播放器支持有差异",
        "subtitle" when t.Codec == "mov_text" => "可直接复制",
        "subtitle" => "MP4 无法原样保存此字幕，请取消选择或改用 MKV",
        "attachment" => "MP4 不支持此字体附件，请取消选择或改用 MKV",
        _ => "暂未确认 MP4 兼容性，请取消选择或改用 MKV"
    };

    public static string MkvCompatibility(MediaTrack t) => t.Codec == "mov_text"
        ? "MP4 文本字幕无法原样复制到 MKV，请取消此轨道（可另加 ASS / SRT）"
        : t.Type is "video" or "audio" or "subtitle" or "attachment" ? "可原样保留" : "暂不支持此轨道，请取消选择";

    public static string ReadSubtitle(SubtitleSource source)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var data = File.ReadAllBytes(source.Path);
        if (source.Encoding == "GB18030") return Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(data);
        if (data.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) return new UnicodeEncoding(false, true, true).GetString(data, 2, data.Length - 2);
        if (data.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) return new UnicodeEncoding(true, true, true).GetString(data, 2, data.Length - 2);
        var offset = data.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        try { return new UTF8Encoding(false, true).GetString(data, offset, data.Length - offset); }
        catch (DecoderFallbackException) { throw new IOException("字幕不是有效的 UTF-8 / UTF-16。请选择 GB18030 并检查文字预览。"); }
    }
}
