using SubtitleStudio;
var fixtures = Path.GetFullPath(args[0]);
var engine = new MediaEngine(Path.GetFullPath(args[1]));
var output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
var settingsPath = Path.Combine(output, "settings.json");
var settings = new UserPreferences { Destination = output, Mode = 2, Container = 1, Font = "Microsoft YaHei", FontSize = "30", Alignment = 1 };
settings.Save(settingsPath); Check(UserPreferences.Load(settingsPath) == settings, "settings round trip");
File.WriteAllText(settingsPath, "broken"); Check(UserPreferences.Load(settingsPath) == new UserPreferences(), "corrupt settings fallback");
var inputs = Path.Combine(output, "inputs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(inputs);
var input = Path.Combine(inputs, "批量 中文.mp4");
File.Copy(Path.Combine(fixtures, "中文 空格 & 视频.mp4"), input, true);
File.Copy(Path.Combine(fixtures, "样式.ass"), Path.ChangeExtension(input, ".ass"), true);
var media = await engine.Probe(input, default);
Job Make(Operation op) => BatchQueue.Create(media, op, "mkv", output, "Microsoft YaHei", 24, 2, []);
var remux = Make(Operation.Remux); var mux = Make(Operation.Mux); var burn = Make(Operation.Burn);
media.Tracks[0].Name = "changed after enqueue";
Check(remux.Media.Tracks[0].Name != media.Tracks[0].Name && remux.Tracks.All(t => remux.Media.Tracks.Contains(t)), "snapshot isolation and track identity");
try { BatchQueue.Create(media with { IsHdr = true }, Operation.Burn, "mp4", output, "Microsoft YaHei", 24, 2, []); throw new Exception("HDR accepted"); } catch (IOException ex) { Check(ex.Message.Contains("HDR"), "HDR batch burning blocked"); }
var invalidTracks = new List<MediaTrack>(media.Tracks) { new() { Index = 99, Type = "subtitle", Codec = "ass" } };
try { BatchQueue.Create(media with { Tracks = invalidTracks }, Operation.Remux, "mp4", output, "Microsoft YaHei", 24, 2, []); throw new Exception("Incompatible tracks accepted"); } catch (IOException) { Console.WriteLine("PASS incompatible tracks blocked"); }
File.Copy(Path.Combine(fixtures, "中文字幕.srt"), Path.ChangeExtension(input, ".srt"), true);
try { Make(Operation.Burn); throw new Exception("Ambiguous subtitles accepted"); } catch (IOException) { Console.WriteLine("PASS ambiguous subtitles blocked"); }
Check(Make(Operation.Mux).Subtitles.Count == 2, "mux matches both subtitles");
try { BatchQueue.Create(media with { IsHdr = true }, Operation.Burn, "mp4", output, "Microsoft YaHei", 24, 2, []); throw new Exception("HDR accepted"); } catch (IOException) { Console.WriteLine("PASS unsafe batch rejected"); }
var broken = remux with { Media = remux.Media with { Path = Path.Combine(output, "missing.mp4") } };
var entries = new[] { new QueueEntry("broken", broken), new QueueEntry("remux", remux), new QueueEntry("mux", mux), new QueueEntry("burn", burn) };
var runner = new JobRunner(engine);
await BatchQueue.Run(entries, (j, t) => runner.Execute(j, "full", t, (_, _) => {}), default);
Check(!entries[0].Finished && entries.Skip(1).All(e => e.Finished), "real remux mux burn continue after failure");
foreach(var entry in entries.Skip(1)) {
 var path = entry.State[5..];
 await engine.Run("ffmpeg", ["-v", "error", "-xerror", "-i", path, "-map", "0:v", "-map", "0:a?", "-f", "null", "-"], default);
}
Console.WriteLine("PASS strict output decoding");
int retries = 0;
await BatchQueue.Run(entries, (_, _) => { retries++; return Task.FromResult("retry"); }, default);
Check(retries == 1, "retry skips completed jobs");
var cancelEntries = new[] { new QueueEntry("first", remux), new QueueEntry("second", mux) };
using var cts = new CancellationTokenSource();
await BatchQueue.Run(cancelEntries, async (_, t) => { cts.Cancel(); await Task.Delay(1, t); return "unexpected"; }, cts.Token);
Check(!cancelEntries.Any(e => e.Finished) && cancelEntries[1].State == "待处理", "cancel stops queue and preserves pending jobs");
Check(!Directory.EnumerateDirectories(output, ".subtitle-studio-*").Any(), "no leftover temporary output");
Console.WriteLine("ALL BATCH AND SETTINGS TESTS PASSED");
