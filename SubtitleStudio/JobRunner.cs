using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SubtitleStudio;

public enum Operation { Remux, Mux, Burn }
public sealed record Job(MediaInfo Media, Operation Operation, string Container, string Directory,
    IReadOnlyList<MediaTrack> Tracks, IReadOnlyList<SubtitleSource> Subtitles, IReadOnlyList<string> Fonts,
    object? BurnSubtitle, MediaTrack? Audio, string Font, int FontSize, int Alignment, double Position);

public sealed class JobRunner(MediaEngine engine)
{
    public static string Describe(Job j) => j.Operation == Operation.Burn
        ? $"视频重新编码：H.264 / CRF 18 / slow；保留分辨率和帧时间。\n音频：{(j.Audio == null ? "无音轨" : j.Audio + (j.Audio.Codec == "aac" ? " · 直接复制" : " · 转换为 AAC 320 kbps"))}\n字幕：{j.BurnSubtitle}"
        : "音视频直接复制；最终保留轨道：\n" + string.Join("\n", j.Tracks) +
          (j.Operation == Operation.Mux ? "\n新增字幕：\n" + string.Join("\n", j.Subtitles.Select(s => s.Name + " · " + s.Language)) + $"\n新增字体附件：{j.Fonts.Count}" : "");

    public static void Validate(Job j)
    {
        if (!j.Tracks.Any(t => t.Type == "video")) throw new IOException("至少保留一条视频轨道。");
        if (j.Operation == Operation.Burn && j.Tracks.Count(t => t.Type == "video") != 1) throw new IOException("烧录请选择且仅选择一条视频轨道。");
        if (j.Operation == Operation.Burn)
        {
            if (j.Media.IsHdr) throw new IOException("检测到 HDR / HLG / 杜比视界。首版仅支持 SDR 烧录，仍可使用无损换封装。");
            if (j.BurnSubtitle == null) throw new IOException("请选择一条烧录字幕。");
            if (j.BurnSubtitle is MediaTrack t && t.Codec is not ("ass" or "ssa" or "subrip" or "mov_text")) throw new IOException("烧录仅支持 ASS、SRT 和 MP4 文本字幕。");
        }
        else if (j.Container == "mp4")
        {
            var errors = j.Tracks.Where(t => !MediaEngine.Mp4Compatibility(t).StartsWith("可")).ToArray();
            if (errors.Length > 0) throw new IOException(string.Join("\n", errors.Select(t => t + "：" + MediaEngine.Mp4Compatibility(t))));
        }
        else if (j.Tracks.Any(t => !MediaEngine.MkvCompatibility(t).StartsWith("可")))
            throw new IOException(string.Join("\n", j.Tracks.Where(t => !MediaEngine.MkvCompatibility(t).StartsWith("可")).Select(t => t + "：" + MediaEngine.MkvCompatibility(t))));
        if (j.Operation == Operation.Mux && j.Subtitles.Count == 0) throw new IOException("请至少添加一份字幕。");
        if (j.Operation == Operation.Mux && j.Subtitles.Count(s => s.Default) + j.Tracks.Count(t => t.Type == "subtitle" && t.Default) > 1)
            throw new IOException("请只设置一条默认字幕；可以取消已有字幕的默认标记。");
        if (j.Operation == Operation.Mux) foreach (var s in j.Subtitles) _ = MediaEngine.ReadSubtitle(s);
        if (j.Operation == Operation.Burn && j.BurnSubtitle is SubtitleSource source) _ = MediaEngine.ReadSubtitle(source);
    }

    public async Task<string> Execute(Job j, string kind, CancellationToken token, Action<string, double> report)
    {
        Validate(j);
        Directory.CreateDirectory(j.Directory);
        var scratch = Path.Combine(j.Directory, ".subtitle-studio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string extension = kind == "preview" ? "png" : j.Container;
        var temporary = Path.Combine(scratch, "output." + extension);
        try
        {
            report("准备", 0);
            var external = new List<string>();
            for (int i = 0; j.Operation == Operation.Mux && i < j.Subtitles.Count; i++)
            {
                var p = Path.Combine(scratch, $"sub{i}" + Path.GetExtension(j.Subtitles[i].Path));
                await File.WriteAllTextAsync(p, MediaEngine.ReadSubtitle(j.Subtitles[i]), new UTF8Encoding(false), token);
                external.Add(p);
            }
            void Progress(string line)
            {
                if (line.StartsWith("out_time_us=") && double.TryParse(line[12..], out var us))
                    report("处理", Math.Clamp(us / 1_000_000 / (kind == "sample" ? Math.Min(10, j.Media.Duration - j.Position) : j.Media.Duration) * 100, 0, 99));
                if (line.Contains("Progress:") && double.TryParse(line.Split(':').Last().Trim().TrimEnd('%'), out var pc)) report("封装", Math.Min(99, pc));
            }
            if (j.Operation != Operation.Burn && j.Container == "mkv")
            {
                using var identified = JsonDocument.Parse(await engine.Run("mkvmerge", ["-J", j.Media.Path], token));
                var all = identified.RootElement.GetProperty("tracks").EnumerateArray().ToArray();
                var args = new List<string> { "--ui-language", "en", "-o", temporary };
                foreach (var (type, flag) in new[] { ("video", "video"), ("audio", "audio"), ("subtitle", "subtitle") })
                {
                    var original = j.Media.Tracks.Where(t => t.Type == type).ToArray();
                    var mkv = all.Where(t => t.GetProperty("type").GetString() == (type == "subtitle" ? "subtitles" : type)).ToArray();
                    if (original.Length != mkv.Length) throw new IOException("轨道识别结果不一致，无法安全映射。");
                    var selected = original.Select((t, i) => (t, id: mkv[i].GetProperty("id").GetInt32())).Where(x => j.Tracks.Contains(x.t)).ToArray();
                    if (selected.Length == 0) args.Add("--no-" + (type == "subtitle" ? "subtitles" : type));
                    else { args.Add("--" + flag + "-tracks"); args.Add(string.Join(',', selected.Select(x => x.id))); }
                    foreach (var (t, id) in selected)
                        args.AddRange(["--track-name", $"{id}:{t.Name}", "--language", $"{id}:{t.Language}", "--default-track-flag", $"{id}:{(t.Default ? 1 : 0)}"]);
                }
                var attachments = identified.RootElement.GetProperty("attachments").EnumerateArray().ToArray();
                var originalAttachments = j.Media.Tracks.Where(t => t.Type == "attachment").ToArray();
                if (originalAttachments.Length != attachments.Length) throw new IOException("附件识别结果不一致，无法安全映射。");
                var keptAttachments = originalAttachments.Select((t, i) => (t, id: attachments[i].GetProperty("id").GetInt32())).Where(x => j.Tracks.Contains(x.t)).ToArray();
                if (keptAttachments.Length == 0) args.Add("--no-attachments");
                else args.AddRange(["--attachments", string.Join(',', keptAttachments.Select(a => a.id))]);
                args.Add(j.Media.Path);
                if (j.Operation == Operation.Mux)
                {
                    // Establish the remuxed timeline before adding external subtitles. MP4
                    // AAC priming can move the common origin; subtitles must move with video.
                    var basePath = Path.Combine(scratch, "base.mkv");
                    args[3] = basePath;
                    await engine.Run("mkvmerge", args, token, Progress);
                    var basis = await engine.Probe(basePath, token);
                    var sourceVideo = j.Tracks.First(t => t.Type == "video");
                    var offsetMs = (basis.Tracks.First(t => t.Type == "video").StartTime - sourceVideo.StartTime + j.Media.Start) * 1000;
                    args = ["--ui-language", "en", "-o", temporary, basePath];
                    for (int i = 0; i < external.Count; i++)
                        args.AddRange(["--language", "0:" + j.Subtitles[i].Language, "--track-name", "0:" + j.Subtitles[i].Name,
                            "--default-track-flag", "0:" + (j.Subtitles[i].Default ? "1" : "0"), "--sync", "0:" + offsetMs.ToString("0.######", CultureInfo.InvariantCulture), external[i]]);
                    foreach (var font in j.Fonts) args.AddRange(["--attachment-mime-type", Path.GetExtension(font).Equals(".otf", StringComparison.OrdinalIgnoreCase) ? "font/otf" : "font/ttf", "--attach-file", font]);
                }
                await engine.Run("mkvmerge", args, token, Progress);
            }
            else
            {
                var args = new List<string> { "-nostdin", "-v", "warning", "-progress", "pipe:1", "-i", j.Media.Path };
                if (j.Operation == Operation.Burn)
                {
                    string subtitle;
                    if (j.BurnSubtitle is SubtitleSource source)
                    {
                        subtitle = "render" + Path.GetExtension(source.Path).ToLowerInvariant();
                        await File.WriteAllTextAsync(Path.Combine(scratch, subtitle), MediaEngine.ReadSubtitle(source), new UTF8Encoding(false), token);
                    }
                    else
                    {
                        var t = (MediaTrack)j.BurnSubtitle!;
                        subtitle = "render" + (t.Codec is "ass" or "ssa" ? ".ass" : ".srt");
                        if (Path.GetExtension(j.Media.Path).Equals(".mkv", StringComparison.OrdinalIgnoreCase))
                        {
                            using var identified = JsonDocument.Parse(await engine.Run("mkvmerge", ["-J", j.Media.Path], token));
                            var ids = identified.RootElement.GetProperty("tracks").EnumerateArray().Where(x => x.GetProperty("type").GetString() == "subtitles").ToArray();
                            var subs = j.Media.Tracks.Where(x => x.Type == "subtitle").ToArray();
                            if (ids.Length != subs.Length) throw new IOException("字幕轨道识别不一致。");
                            var id = ids[Array.IndexOf(subs, t)].GetProperty("id").GetInt32();
                            await engine.Run("mkvextract", [j.Media.Path, "tracks", id + ":" + Path.Combine(scratch, subtitle)], token);
                        }
                        else await engine.Run("ffmpeg", ["-nostdin", "-v", "error", "-i", j.Media.Path, "-map", "0:" + t.Index, "-c:s", t.Codec == "mov_text" ? "srt" : "copy", Path.Combine(scratch, subtitle)], token);
                    }
                    Directory.CreateDirectory(Path.Combine(scratch, "fonts"));
                    foreach (var font in j.Fonts) File.Copy(font, Path.Combine(scratch, "fonts", Guid.NewGuid().ToString("N") + Path.GetExtension(font)));
                    foreach (var attachment in j.Media.Tracks.Where(t => t.Type == "attachment" && (t.Codec is "ttf" or "otf" || Path.GetExtension(t.Name).ToLowerInvariant() is ".ttf" or ".otf")))
                    {
                        var fontPath = Path.Combine(scratch, "fonts", attachment.Index + (attachment.Codec == "otf" ? ".otf" : ".ttf"));
                        // Dumping attachments is an input option; no output is required, so use a zero-frame null output.
                        await engine.Run("ffmpeg", ["-nostdin", "-v", "error", "-dump_attachment:" + attachment.Index, fontPath, "-i", j.Media.Path, "-map", "0:v:0", "-frames:v", "0", "-f", "null", "-"], token);
                    }
                    var filter = "subtitles=" + subtitle + ":fontsdir=fonts";
                    if (subtitle.EndsWith(".srt"))
                    {
                        if (j.Font.IndexOfAny(['\'', ':', ',', '\\', '[', ']']) >= 0) throw new IOException("字体名称含有不支持的标点。");
                        filter += $":force_style='Fontname={j.Font},Fontsize={j.FontSize},PrimaryColour=&H00FFFFFF,OutlineColour=&H00000000,Outline=2,Shadow=0,Alignment={j.Alignment},MarginV=24'";
                    }
                    if (j.BurnSubtitle is MediaTrack && Path.GetExtension(j.Media.Path).Equals(".mkv", StringComparison.OrdinalIgnoreCase) && j.Media.Start != 0)
                    {
                        var start = j.Media.Start.ToString("R", CultureInfo.InvariantCulture);
                        filter = "setpts=PTS+(" + start + ")/TB," + filter + ",setpts=PTS-(" + start + ")/TB";
                    }
                    args.AddRange(["-map", "0:" + j.Tracks.First(t => t.Type == "video").Index, "-vf", filter]);
                    if (kind is "preview" or "sample") args.AddRange(["-ss", j.Position.ToString(CultureInfo.InvariantCulture)]);
                    if (kind == "preview") args.AddRange(["-frames:v", "1", "-an", "-c:v", "png"]);
                    else
                    {
                        if (j.Audio != null) args.AddRange(["-map", "0:" + j.Audio.Index, "-c:a", j.Audio.Codec == "aac" ? "copy" : "aac"]);
                        if (j.Audio != null && j.Audio.Codec != "aac") args.AddRange(["-b:a", "320k"]);
                        args.AddRange(["-c:v", "libx264", "-crf", "18", "-preset", "slow", "-pix_fmt", "yuv420p", "-fps_mode", "passthrough", "-movflags", "+faststart"]);
                        if (kind == "sample") args.AddRange(["-t", "10"]);
                    }
                }
                else
                {
                    foreach (var t in j.Tracks) args.AddRange(["-map", "0:" + t.Index]);
                    args.AddRange(["-map_metadata", "0", "-map_chapters", "0", "-c", "copy", "-movflags", "+faststart"]);
                    for (var index = 0; index < j.Tracks.Count; index++)
                    {
                        var track = j.Tracks[index];
                        args.AddRange(["-metadata:s:" + index, "title=" + track.Name, "-metadata:s:" + index, "language=" + track.Language,
                            "-disposition:" + index, track.Default ? "+default" : "-default"]);
                    }
                }
                args.Add(temporary);
                await engine.Run("ffmpeg", args, token, Progress, scratch);
            }
            token.ThrowIfCancellationRequested();
            report("检查输出", 99);
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0) throw new IOException("没有生成有效输出。");
            if (kind != "preview")
            {
                var result = await engine.Probe(temporary, token);
                if (!result.Tracks.Any(t => t.Type == "video") || result.Duration <= 0) throw new IOException("输出验证失败，未生成正式文件。");
                if (j.Operation != Operation.Burn && result.Tracks.Count != j.Tracks.Count + (j.Operation == Operation.Mux ? j.Subtitles.Count + j.Fonts.Count : 0)) throw new IOException("输出轨道数量不符，未生成正式文件。");
                var expectedDuration = kind == "sample" ? Math.Min(10, j.Media.Duration - j.Position) : j.Media.Duration;
                if (Math.Abs(result.Duration - expectedDuration) > Math.Max(1, expectedDuration * 0.001)) throw new IOException("输出时长与预期不符，未生成正式文件。");
                var inputVideo = j.Tracks.First(t => t.Type == "video");
                var outputVideo = result.Tracks.First(t => t.Type == "video");
                if (inputVideo.Width != outputVideo.Width || inputVideo.Height != outputVideo.Height) throw new IOException("输出分辨率与源文件不符。");
                if (j.Operation != Operation.Burn)
                {
                    foreach (var type in new[] { "video", "audio" })
                    {
                        var original = j.Tracks.Where(t => t.Type == type).ToArray();
                        var copied = result.Tracks.Where(t => t.Type == type).ToArray();
                        if (original.Length != copied.Length || !original.Select(t => t.Codec).SequenceEqual(copied.Select(t => t.Codec))) throw new IOException("输出音视频编码或轨道不符。");
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            var stem = Path.Combine(j.Directory, Path.GetFileNameWithoutExtension(j.Media.Path) + (kind == "preview" ? "_预览" : kind == "sample" ? "_10秒试输出" : "_输出"));
            for (int n = 0; ; n++)
            {
                var destination = stem + (n == 0 ? "" : "_" + n) + "." + extension;
                try { File.Move(temporary, destination, false); report("完成", 100); return destination; }
                catch (IOException) when (File.Exists(destination)) { }
            }
        }
        finally { try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
