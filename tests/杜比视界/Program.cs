using SubtitleStudio;

if (args.Length != 2)
{
    throw new ArgumentException("传入测试样片目录和引擎目录。");
}
var sampleDirectory = Path.GetFullPath(args[0]);
var engine = new MediaEngine(Path.GetFullPath(args[1]));
var samples = Directory.GetFiles(sampleDirectory, "dovi-*.mp4");
if (samples.Length == 0)
{
    throw new IOException("测试目录中没有 dovi-*.mp4 样片。");
}
var outputDirectory = Path.Combine(sampleDirectory, "dovi-output");
Directory.CreateDirectory(outputDirectory);
var subtitlePath = Path.Combine(outputDirectory, "test.srt");
await File.WriteAllTextAsync(subtitlePath, "1\n00:00:00,000 --> 00:00:05,000\nHDR 字幕测试\n");
foreach (var sample in samples)
{
    var media = await engine.Probe(sample, CancellationToken.None);
    if (!media.IsHdr)
    {
        throw new Exception("未识别杜比视界文件 " + Path.GetFileName(sample));
    }
    var job = new Job(media, Operation.Burn, "mp4", outputDirectory,
        [media.Tracks.First(track => track.Type == "video")], [], [], new SubtitleSource { Path = subtitlePath }, null, "Microsoft YaHei", 24, 2, 0);
    try
    {
        JobRunner.Validate(job);
        throw new Exception("未阻止烧录 " + Path.GetFileName(sample));
    }
    catch (IOException exception) when (exception.Message.Contains("HDR"))
    {
        Console.WriteLine("PASS " + Path.GetFileName(sample) + " 识别并要求确认烧录风险");
    }
    foreach (var kind in new[] { "preview", "full" })
    {
        var output = await new JobRunner(engine).Execute(job with { HdrRiskAccepted = true }, kind, default, (_, _) => {});
        await engine.Run("ffmpeg", ["-v", "error", "-xerror", "-i", output, "-f", "null", "-"], default);
        Console.WriteLine("PASS " + Path.GetFileName(sample) + " confirmed " + kind + " and strict decode");
    }
}
