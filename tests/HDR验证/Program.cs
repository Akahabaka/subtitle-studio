using SubtitleStudio;
var engine = new MediaEngine(Path.GetFullPath(args[0]));
var folder = Path.GetFullPath(args[1]);
Directory.CreateDirectory(folder);
var subtitle = Path.Combine(folder, "test.srt");
await File.WriteAllTextAsync(subtitle, "1\n00:00:00,100 --> 00:00:02,000\nHDR 字幕测试\n");
var runner = new JobRunner(engine);
foreach (var transfer in new[] { "smpte2084", "arib-std-b67" })
{
    var input = Path.Combine(folder, transfer + ".mp4");
    await engine.Run("ffmpeg", ["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25:duration=3", "-c:v", "libx265", "-preset", "ultrafast", "-pix_fmt", "yuv420p10le", "-x265-params", "colorprim=bt2020:transfer=" + transfer + ":colormatrix=bt2020nc", "-color_primaries", "bt2020", "-color_trc", transfer, "-colorspace", "bt2020nc", input], default);
    var media = await engine.Probe(input, default);
    if (!media.IsHdr) throw new Exception("HDR not detected");
    var job = new Job(media, Operation.Burn, "mp4", folder, media.Tracks, [], [],
        new SubtitleSource { Path = subtitle }, null, "Microsoft YaHei", 24, 2, 0);
    JobRunner.Validate(job, requireHdrConfirmation: false);
    foreach (var kind in new[] { "preview", "sample", "full" })
    {
        try { await runner.Execute(job, kind, default, (_, _) => {}); throw new Exception("Unconfirmed HDR executed"); }
        catch (IOException ex) when (ex.Message.Contains("HDR")) { }
        if (Directory.EnumerateDirectories(folder, ".subtitle-studio-*").Any()) throw new Exception("Unconfirmed task created temporary files");
        var output = await runner.Execute(job with { HdrRiskAccepted = true }, kind, default, (_, _) => {});
        await engine.Run("ffmpeg", ["-v", "error", "-xerror", "-i", output, "-f", "null", "-"], default);
        if (kind != "preview" && (await engine.Probe(output, default)).Tracks.First(t => t.Type == "video").Codec != "h264") throw new Exception("Unexpected output codec");
        Console.WriteLine("PASS " + transfer + " " + kind + " confirmation and strict decode");
    }
    try { JobRunner.Validate(job); throw new Exception("Confirmation persisted"); }
    catch (IOException ex) when (ex.Message.Contains("HDR")) { }
    JobRunner.Validate(job with { Operation = Operation.Remux });
}
Console.WriteLine("ALL HDR TESTS PASSED");

