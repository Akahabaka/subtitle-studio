using SubtitleStudio;
using System.Text;
var root = Path.GetFullPath(args[0]);
var engine = new MediaEngine(Path.GetFullPath(args[1]));
var runner = new JobRunner(engine);
var output = Path.Combine(root, "test-output");
Directory.CreateDirectory(output);
var source = await engine.Probe(Path.Combine(root, "fixtures", "中文 空格 & 视频.mp4"), default);
var ass = new SubtitleSource { Path = Path.Combine(root, "fixtures", "样式.ass"), Name = "特效 ASS", Default = true };
var srt = new SubtitleSource { Path = Path.Combine(root, "fixtures", "中文字幕.srt"), Name = "中文 SRT" };
Job Make(MediaInfo m, Operation operation, string container) => new(m, operation, container, output, m.Tracks, [], [], null, m.Tracks.FirstOrDefault(t => t.Type == "audio" && t.Default), "Microsoft YaHei", 24, 2, 2);
async Task<string> Run(Job j, string kind = "full") { var p = await runner.Execute(j, kind, default, (_, _) => {}); Console.WriteLine("PASS " + kind + " " + p); return p; }
try {
 var mkv = await Run(Make(source, Operation.Remux, "mkv"));
 var mkvInfo = await engine.Probe(mkv, default);
 var mp4 = await Run(Make(mkvInfo, Operation.Remux, "mp4"));
 foreach (var type in new[] { "v", "a:0", "a:1" }) {
  async Task<string> Hash(string path) => await engine.Run("ffmpeg", ["-v", "error", "-i", path, "-map", "0:" + type, "-c", "copy", "-f", "hash", "-hash", "sha256", "-"], default);
  if (await Hash(source.Path) != await Hash(mp4)) throw new Exception("Encoded stream mismatch " + type);
  Console.WriteLine("PASS encoded stream hash " + type);
 }
 var mux = await Run(Make(source, Operation.Mux, "mkv") with { Subtitles = new[] { ass, srt } });
 var muxInfo = await engine.Probe(mux, default);
 if (muxInfo.Tracks.Count(t => t.Type == "subtitle") != 2) throw new Exception("Missing subtitle");
 var burn = Make(source, Operation.Burn, "mp4") with { BurnSubtitle = ass, Subtitles = new[] { ass } };
 await Run(burn, "preview"); await Run(burn, "sample"); await Run(burn);
 await Run(Make(muxInfo, Operation.Burn, "mp4") with { BurnSubtitle = muxInfo.Tracks.First(t => t.Type == "subtitle") }, "sample");
 Console.WriteLine("ALL INITIAL INTEGRATION TESTS PASSED");
} finally { File.WriteAllText(Path.Combine(output, "engine-log.txt"), engine.Log); }
