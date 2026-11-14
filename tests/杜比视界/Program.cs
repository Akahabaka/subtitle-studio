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
foreach (var sample in samples)
{
    var media = await engine.Probe(sample, CancellationToken.None);
    if (!media.IsHdr)
    {
        throw new Exception("未识别杜比视界文件 " + Path.GetFileName(sample));
    }
    var job = new Job(media, Operation.Burn, "mp4", sampleDirectory,
        [media.Tracks.First(track => track.Type == "video")], [], [], null, null, "Microsoft YaHei", 24, 2, 0);
    try
    {
        JobRunner.Validate(job);
        throw new Exception("未阻止烧录 " + Path.GetFileName(sample));
    }
    catch (IOException exception) when (exception.Message.Contains("HDR / HLG / 杜比视界"))
    {
        Console.WriteLine("PASS " + Path.GetFileName(sample) + " 识别并拦截 SDR 烧录");
    }
}
