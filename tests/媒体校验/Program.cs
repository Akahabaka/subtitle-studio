using SubtitleStudio;
var engine = new MediaEngine(Path.GetFullPath(args[0]));
var folder = Path.GetFullPath(args[1]); Directory.CreateDirectory(folder);
var source = Path.Combine(folder, "long-audio.mp4");
await engine.Run("ffmpeg", ["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25:duration=3", "-f", "lavfi", "-i", "sine=duration=3", "-f", "lavfi", "-i", "sine=duration=7", "-map", "0:v", "-map", "1:a", "-map", "2:a", "-c:v", "libx264", "-c:a", "aac", source], default);
var subtitle = Path.Combine(folder, "late.srt");
await File.WriteAllTextAsync(subtitle, "1\n00:00:01,000 --> 00:00:06,000\n中文测试\n");
var sub = new SubtitleSource { Path = subtitle, Name = "late" };
var media = await engine.Probe(source, default);
Job Make(MediaInfo m, Operation op, string container) => new(m, op, container, folder, m.Tracks.Take(2).ToArray(), [sub], [], sub, m.Tracks.First(t => t.Type == "audio"), "Microsoft YaHei", 24, 2, 0);
var runner = new JobRunner(engine);
try { JobRunner.OutputBaseName(Make(media, Operation.Burn, "mp4") with { OutputName = new string('a', 251) }); throw new Exception("Oversize name accepted"); }
catch (IOException) { Console.WriteLine("PASS suffix and numbering length reserved"); }
async Task<string> Run(Job job, string kind = "full")
{
    var result = await runner.Execute(job, kind, default, (_, _) => {});
    if (kind != "preview") await engine.Run("ffmpeg", ["-v", "error", "-xerror", "-i", result, "-map", "0:v", "-map", "0:a?", "-f", "null", "-"], default);
    Console.WriteLine("PASS " + kind + " " + Path.GetFileName(result));
    return result;
}
await Run(Make(media, Operation.Remux, "mp4"));
await Run(Make(media, Operation.Remux, "mkv"));
var mux = await Run(Make(media, Operation.Mux, "mkv"));
var muxInfo = await engine.Probe(mux, default);
if (muxInfo.Duration < 5.9) throw new Exception("Late subtitle was truncated");
var times = await engine.TrackTimes(mux, default);
if (times[muxInfo.Tracks.First(t => t.Type == "video").Index].End > 3.2) throw new Exception("Video unexpectedly extended");
Console.WriteLine("PASS long subtitle retained without extending video");
foreach (var angle in new[] { 90, 180, 270 })
{
    var rotated = Path.Combine(folder, "rotated-" + angle + ".mp4");
    await engine.Run("ffmpeg", ["-y", "-v", "error", "-display_rotation:v:0", angle.ToString(), "-i", source, "-map", "0:v", "-map", "0:a:0", "-c", "copy", rotated], default);
    var info = await engine.Probe(rotated, default);
    if (Math.Abs(info.Tracks.First(t => t.Type == "video").Rotation) < 1) throw new Exception("Fixture lacks rotation metadata");
    var job = Make(info, Operation.Burn, "mp4");
    await Run(job, "preview");
    await Run(job, "sample");
    var output = await Run(job);
    var video = (await engine.Probe(output, default)).Tracks.First(t => t.Type == "video");
    if (video.Width != (angle == 180 ? 320 : 180) || video.Height != (angle == 180 ? 180 : 320)) throw new Exception("Rotation dimensions incorrect");
    Console.WriteLine("PASS rotation " + angle);
}
Console.WriteLine("ALL MEDIA VALIDATION TESTS PASSED");



var shortAudio = Path.Combine(folder, "short-audio.mp4");
await engine.Run("ffmpeg", ["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25:duration=20", "-f", "lavfi", "-i", "sine=duration=3", "-c:v", "libx264", "-c:a", "aac", shortAudio], default);
var shortInfo = await engine.Probe(shortAudio, default);
var silentSample = await Run(Make(shortInfo, Operation.Burn, "mp4") with { Position = 15 }, "sample");
if ((await engine.Probe(silentSample, default)).Tracks.Any(t => t.Type == "audio")) throw new Exception("Unexpected audio in silent interval");
Console.WriteLine("PASS sample after audio end");
