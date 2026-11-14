using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace SubtitleStudio;

public sealed class QueueEntry(string source, Job? job, string state = "待处理") : INotifyPropertyChanged
{
    public string Source { get; } = source;
    public Job? Job { get; } = job;
    public string OperationName => Job?.Operation switch { Operation.Remux => "无损换封装", Operation.Mux => "字幕内封", Operation.Burn => "字幕烧录", _ => "未加入" };
    public string Destination => Job?.Directory ?? "";
    private string state = state;
    public string State { get => state; set { state = value; PropertyChanged?.Invoke(this, new(nameof(State))); } }
    public bool Finished { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class BatchQueue
{
    // Preserve track identity within the cloned media: MKV mapping uses Contains.
    public static Job Freeze(Job job)
    {
        var media = JsonSerializer.Deserialize<MediaInfo>(JsonSerializer.Serialize(job.Media))!;
        MediaTrack Track(MediaTrack t) => media.Tracks.Single(x => x.Index == t.Index);
        SubtitleSource Sub(SubtitleSource s) => JsonSerializer.Deserialize<SubtitleSource>(JsonSerializer.Serialize(s))!;
        return job with { Media = media, Tracks = job.Tracks.Select(Track).ToArray(), Subtitles = job.Subtitles.Select(Sub).ToArray(),
            Fonts = job.Fonts.ToArray(), Audio = job.Audio == null ? null : Track(job.Audio),
            BurnSubtitle = job.BurnSubtitle switch { MediaTrack t => Track(t), SubtitleSource s => Sub(s), _ => null } };
    }

    public static Job Create(MediaInfo media, Operation operation, string container, string directory, string font, int size, int alignment, string[] fonts)
    {
        var stem = Path.GetFileNameWithoutExtension(media.Path);
        var subs = Directory.EnumerateFiles(Path.GetDirectoryName(media.Path)!)
            .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), stem, StringComparison.OrdinalIgnoreCase)
                && Path.GetExtension(p).ToLowerInvariant() is ".ass" or ".srt")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => new SubtitleSource { Path = p, Name = Path.GetFileNameWithoutExtension(p) }).ToArray();
        object? burn = null;
        if (operation == Operation.Burn)
        {
            if (subs.Length > 1) throw new IOException("同名字幕不止一份，请单独配置后加入队列。");
            var embedded = media.Tracks.Where(t => t.Type == "subtitle").ToArray();
            burn = subs.Length == 1 ? subs[0] : embedded.Length == 1 ? embedded[0] : null;
            if (burn == null) throw new IOException("没有唯一匹配字幕，请单独配置后加入队列。");
        }
        var job = new Job(media, operation, operation == Operation.Mux ? "mkv" : operation == Operation.Burn ? "mp4" : container,
            directory, media.Tracks.ToArray(), operation == Operation.Mux ? subs : [], fonts, burn,
            media.Tracks.FirstOrDefault(t => t.Type == "audio" && t.Default) ?? media.Tracks.FirstOrDefault(t => t.Type == "audio"), font, size, alignment, 0);
        JobRunner.Validate(job);
        return Freeze(job);
    }

    public static async Task Run(IEnumerable<QueueEntry> entries, Func<Job, CancellationToken, Task<string>> execute, CancellationToken token)
    {
        foreach (var entry in entries.Where(e => e.Job != null && !e.Finished))
        {
            if (token.IsCancellationRequested) break;
            entry.State = "处理中";
            try { var result = await execute(entry.Job!, token); entry.Finished = true; entry.State = "完成 · " + result; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { entry.State = "已取消，可重试"; break; }
            catch (Exception ex) { entry.State = "失败 · " + ex.Message; }
        }
    }
}
