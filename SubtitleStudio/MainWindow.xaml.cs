using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
namespace SubtitleStudio;
public partial class MainWindow : Window
{
    private readonly MediaEngine engine = new(Path.Combine(AppContext.BaseDirectory, "engines"));
    private readonly ObservableCollection<SubtitleSource> subtitles = [];
    private readonly List<string> fonts = [];
    private readonly ObservableCollection<QueueEntry> queue = [];
    private MediaInfo? media;
    private CancellationTokenSource? cancellation;
    private readonly Stopwatch elapsed = new();
    private string errorLog = "";
    public MainWindow()
    {
        InitializeComponent(); Subtitles.ItemsSource = subtitles;
        Queue.ItemsSource = queue;
        var saved = UserPreferences.Load(UserPreferences.FilePath);
        Destination.Text = saved.Destination ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        Mode.SelectedIndex = Math.Clamp(saved.Mode, 0, 2);
        if (Mode.SelectedIndex == 0) Container.SelectedIndex = Math.Clamp(saved.Container, 0, 1);
        FontName.Text = saved.Font ?? "Microsoft YaHei";
        FontSizeBox.Text = int.TryParse(saved.FontSize, out var savedSize) && savedSize is >= 6 and <= 200 ? saved.FontSize : "24";
        Alignment.SelectedIndex = Math.Clamp(saved.Alignment, 0, 2);
        ModeChanged(this, null!);
        Loaded += (_, _) => { if (engine.Missing.Length > 0) Status.Text = "缺少处理引擎：" + string.Join("、", engine.Missing) + "。请完整解压软件包。"; };
        Closing += (_, e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); Status.Text = "正在取消任务，请稍后关闭窗口。"; } else SavePreferences(); };
    }
    private async Task LoadVideo(string path)
    {
        if (cancellation != null) return;
        cancellation = new(); Settings.IsEnabled = false;
        try
        {
            Status.Text = "正在读取媒体轨道…";
            var candidate = await engine.Probe(path, cancellation.Token);
            if (!candidate.Tracks.Any(t => t.Type == "video")) throw new IOException("文件中没有视频轨道。");
            media = candidate; Video.Text = path; Tracks.ItemsSource = media.Tracks;
            MediaSummary.Text = $"时长 {TimeSpan.FromSeconds(media.Duration):hh\\:mm\\:ss} · {media.Tracks.Count} 条轨道 / 附件 · {(media.IsHdr ? "HDR / HLG / 杜比视界，不能烧录" : "SDR")}";
            Audio.ItemsSource = media.Tracks.Where(t => t.Type == "audio").ToArray();
            Audio.SelectedItem = media.Tracks.FirstOrDefault(t => t.Type == "audio" && t.Default) ?? media.Tracks.FirstOrDefault(t => t.Type == "audio");
            RefreshSubtitles(); UpdateCompatibility(true); Status.Text = "已读取，可选择保留轨道和添加字幕。";
        }
        catch (Exception ex) { Failure(ex); }
        finally { cancellation.Dispose(); cancellation = null; Settings.IsEnabled = true; }
    }
    private async void ChooseVideo(object sender, RoutedEventArgs e) { var d = new OpenFileDialog { Filter = "视频|*.mkv;*.mp4" }; if (d.ShowDialog() == true) await LoadVideo(d.FileName); }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (cancellation != null || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var videos = paths.Where(p => Path.GetExtension(p).ToLowerInvariant() is ".mp4" or ".mkv").ToArray();
        if (videos.Length > 1) { await ImportBatch(videos); return; }
        if (videos.Length == 1) await LoadVideo(videos[0]);
        foreach (var p in paths)
        {
            if (Path.GetExtension(p).ToLowerInvariant() is ".ass" or ".srt") AddSubtitle(p);
            if (Path.GetExtension(p).ToLowerInvariant() is ".ttf" or ".otf") AddFont(p);
        }
    }
    private void ChooseSubtitle(object sender, RoutedEventArgs e) { var d = new OpenFileDialog { Filter = "字幕|*.ass;*.srt", Multiselect = true }; if (d.ShowDialog() == true) foreach (var p in d.FileNames) AddSubtitle(p); }
    private void AddSubtitle(string path) { if (!subtitles.Any(s => s.Path == path)) subtitles.Add(new() { Path = path, Name = Path.GetFileNameWithoutExtension(path) }); RefreshSubtitles(); }
    private void RemoveSubtitle(object sender, RoutedEventArgs e) { if (Subtitles.SelectedItem is SubtitleSource s) subtitles.Remove(s); RefreshSubtitles(); }
    private void SubtitlesChanged(object sender, SelectionChangedEventArgs e) { }
    private void RefreshSubtitles()
    {
        var previous = BurnSubtitle.SelectedItem;
        BurnSubtitle.ItemsSource = (media?.Tracks.Where(t => t.Type == "subtitle").Cast<object>() ?? []).Concat(subtitles).ToArray();
        BurnSubtitle.SelectedItem = previous;
        if (BurnSubtitle.SelectedIndex < 0) BurnSubtitle.SelectedIndex = 0;
    }
    private void ChooseFonts(object sender, RoutedEventArgs e) { var d = new OpenFileDialog { Filter = "字体|*.ttf;*.otf", Multiselect = true }; if (d.ShowDialog() == true) foreach (var p in d.FileNames) AddFont(p); }
    private void AddFont(string path) { if (!fonts.Contains(path)) fonts.Add(path); FontStatus.Text = "新增字体：" + string.Join("、", fonts.Select(Path.GetFileName)); }
    private void ClearFonts(object sender, RoutedEventArgs e) { fonts.Clear(); FontStatus.Text = "未添加字体附件"; }
    private async void InspectSubtitle(object sender, RoutedEventArgs e)
    {
        if (cancellation != null) return;
        try
        {
            Subtitles.CommitEdit(); Subtitles.CommitEdit(DataGridEditingUnit.Row, true);
            object? source = Mode.SelectedIndex == 2 ? BurnSubtitle.SelectedItem : Subtitles.SelectedItem;
            source ??= Tracks.SelectedItem is MediaTrack { Type: "subtitle" } t ? t : subtitles.FirstOrDefault();
            if (source == null) throw new IOException("请选中一份外部或已有字幕。");
            cancellation = new(); Settings.IsEnabled = false; StartButton.IsEnabled = false; BatchStartButton.IsEnabled = false;
            Status.Text = "正在检查字幕文字、字体引用和源视频字体附件…";
            var message = await SubtitleInspector.Inspect(engine, media, source, fonts, cancellation.Token);
            ShowText("字幕文字与字体检查", message);
            Status.Text = "字幕与字体检查完成，可补充缺失字体。";
        }
        catch (Exception ex) { Failure(ex); }
        finally { cancellation?.Dispose(); cancellation = null; Settings.IsEnabled = true; StartButton.IsEnabled = true; BatchStartButton.IsEnabled = true; }
    }
    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Container == null || BurnPanel == null) return;
        Container.IsEnabled = Mode.SelectedIndex == 0;
        if (Mode.SelectedIndex == 1) Container.SelectedIndex = 0;
        if (Mode.SelectedIndex == 2) Container.SelectedIndex = 1;
        BurnPanel.Visibility = Mode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCompatibility(false);
    }
    private void UpdateCompatibility(bool defaults)
    {
        if (media == null) return;
        foreach (var t in media.Tracks)
        {
            t.Compatibility = Mode.SelectedIndex == 2 ? "烧录使用下方字幕、音轨设置" : Container.SelectedIndex == 1 ? MediaEngine.Mp4Compatibility(t) : MediaEngine.MkvCompatibility(t);
            if (defaults) t.Selected = Mode.SelectedIndex == 2 || t.Compatibility.StartsWith("可");
        }
        Tracks.Items.Refresh();
    }
    private Job Snapshot()
    {
        Tracks.CommitEdit(); Tracks.CommitEdit(DataGridEditingUnit.Row, true); Subtitles.CommitEdit(); Subtitles.CommitEdit(DataGridEditingUnit.Row, true);
        if (media == null) throw new IOException("请先选择视频。");
        if (!int.TryParse(FontSizeBox.Text, out var size) || size < 6 || size > 200) throw new IOException("字号范围为 6–200。");
        if (!double.TryParse(Position.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pos) || pos < 0 || pos >= media.Duration) throw new IOException("时间点需在视频时长范围内，以秒为单位。");
        return new(media, (Operation)Mode.SelectedIndex, Container.SelectedIndex == 0 ? "mkv" : "mp4", Path.GetFullPath(Destination.Text), media.Tracks.Where(t => t.Selected).ToArray(), subtitles.Where(s => s.Selected).ToArray(), fonts.ToArray(), BurnSubtitle.SelectedItem, Audio.SelectedItem as MediaTrack, FontName.Text, size, Alignment.SelectedIndex switch { 1 => 8, 2 => 5, _ => 2 }, pos);
    }
    private async Task Process(string kind)
    {
        if (cancellation != null) return;
        try
        {
            var job = Snapshot(); JobRunner.Validate(job); SavePreferences();
            if (kind == "full" && MessageBox.Show(this, JobRunner.Describe(job) + "\n\n输出位置：" + job.Directory + "\n将另存为新文件，继续处理？", "执行前检查", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            cancellation = new(); Settings.IsEnabled = false; StartButton.IsEnabled = false; BatchStartButton.IsEnabled = false; elapsed.Restart();
            var result = await new JobRunner(engine).Execute(job, kind, cancellation.Token, (phase, p) => Dispatcher.Invoke(() => { Progress.Value = p; Status.Text = $"{phase} · {p:F0}% · 已用时 {elapsed.Elapsed:hh\\:mm\\:ss}"; }));
            Status.Text = "完成 · " + result;
            if (kind == "preview") { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(result); bitmap.EndInit(); new Window { Title = "字幕画面预览", Owner = this, Width = 960, Height = 620, Content = new Image { Source = bitmap, Stretch = Stretch.Uniform } }.Show(); }
        }
        catch (Exception ex) { Failure(ex); }
        finally { elapsed.Stop(); cancellation?.Dispose(); cancellation = null; Settings.IsEnabled = true; StartButton.IsEnabled = true; BatchStartButton.IsEnabled = true; }
    }
    private void SavePreferences()
    {
        try { new UserPreferences { Mode = Mode.SelectedIndex, Container = Container.SelectedIndex, Destination = Destination.Text,
            Font = FontName.Text, FontSize = FontSizeBox.Text, Alignment = Alignment.SelectedIndex }.Save(UserPreferences.FilePath); }
        catch (Exception ex) { errorLog += "保存设置失败：" + ex + "\n"; Status.Text = "无法保存设置：" + ex.Message; }
    }
    private void AddCurrent(object sender, RoutedEventArgs e)
    {
        if (cancellation != null) return;
        try { var job = Snapshot(); JobRunner.Validate(job); queue.Add(new(job.Media.Path, BatchQueue.Freeze(job))); SavePreferences(); Status.Text = "已将当前配置加入队列，可继续配置下一个视频。"; }
        catch (Exception ex) { Failure(ex); }
    }
    private async void ChooseBatch(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "视频|*.mkv;*.mp4", Multiselect = true };
        if (dialog.ShowDialog(this) == true) await ImportBatch(dialog.FileNames);
    }
    private async Task ImportBatch(string[] paths)
    {
        if (cancellation != null) return;
        try
        {
            if (!int.TryParse(FontSizeBox.Text, out var size) || size is < 6 or > 200) throw new IOException("字号范围为 6–200。");
            var destination = Path.GetFullPath(Destination.Text);
            var operation = (Operation)Mode.SelectedIndex;
            var container = Container.SelectedIndex == 0 ? "mkv" : "mp4";
            var font = FontName.Text; var alignment = Alignment.SelectedIndex switch { 1 => 8, 2 => 5, _ => 2 };
            cancellation = new(); Settings.IsEnabled = false; StartButton.IsEnabled = false; BatchStartButton.IsEnabled = false;
            SavePreferences();
            foreach (var path in paths)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Status.Text = "正在检查批量文件：" + Path.GetFileName(path);
                try
                {
                    var input = await engine.Probe(path, cancellation.Token);
                    var job = BatchQueue.Create(input, operation, container, destination, font, size, alignment, fonts.ToArray());
                    queue.Add(new(path, job));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { queue.Add(new(path, null, "需单独配置 · " + ex.Message)); errorLog += path + "\n" + ex + "\n"; }
            }
            Status.Text = "批量检查完成。请查看队列；需要单独配置的文件不会执行。";
        }
        catch (Exception ex) { Failure(ex); }
        finally { cancellation?.Dispose(); cancellation = null; Settings.IsEnabled = true; StartButton.IsEnabled = true; BatchStartButton.IsEnabled = true; }
    }
    private void RemoveQueue(object sender, RoutedEventArgs e) { if (Queue.SelectedItem is QueueEntry item) queue.Remove(item); }
    private void ClearCompleted(object sender, RoutedEventArgs e) { foreach (var item in queue.Where(x => x.Finished).ToArray()) queue.Remove(item); }
    private void InspectQueue(object sender, RoutedEventArgs e)
    {
        if (Queue.SelectedItem is QueueEntry { Job: { } job }) ShowText("队列任务设置", job.Media.Path + "\n\n" + JobRunner.Describe(job) + "\n\n输出位置：" + job.Directory);
    }
    private async void StartBatch(object sender, RoutedEventArgs e)
    {
        if (cancellation != null) return;
        var pending = queue.Where(x => x.Job != null && !x.Finished).ToArray();
        if (pending.Length == 0) { Status.Text = "请先添加可执行的队列任务。"; return; }
        if (MessageBox.Show(this, $"将依次处理 {pending.Length} 个任务。失败后继续下一个；取消会停止整个队列。已完成的任务不会重复执行。\n\n" +
            string.Join("\n", pending.Take(15).Select(x => Path.GetFileName(x.Source) + " · " + x.OperationName + " → " + x.Destination)) +
            (pending.Length > 15 ? $"\n……另有 {pending.Length - 15} 个任务，请在队列中核对。" : "") +
            "\n\n字幕烧录会重新编码视频。每个任务使用加入队列时的设置，可通过“查看任务设置”核对。", "批量执行前检查", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        cancellation = new(); Settings.IsEnabled = false; StartButton.IsEnabled = false; BatchStartButton.IsEnabled = false; elapsed.Restart();
        try
        {
            int number = 0;
            await BatchQueue.Run(pending, async (job, token) =>
            {
                number++;
                try { return await new JobRunner(engine).Execute(job, "full", token, (phase, progress) => Dispatcher.Invoke(() =>
                    { Progress.Value = progress; Status.Text = $"队列 {number}/{pending.Length} · {Path.GetFileName(job.Media.Path)} · {phase} {progress:F0}% · {elapsed.Elapsed:hh\\:mm\\:ss}"; })); }
                catch (Exception ex) { errorLog += job.Media.Path + "\n" + ex + "\n"; throw; }
            }, cancellation.Token);
            Status.Text = $"{(cancellation.IsCancellationRequested ? "队列已停止" : "队列执行结束")} · 本次完成 {pending.Count(x => x.Finished)}/{pending.Length}。未完成任务可重试，详情见队列。";
        }
        catch (Exception ex) { Failure(ex); }
        finally { elapsed.Stop(); cancellation.Dispose(); cancellation = null; Settings.IsEnabled = true; StartButton.IsEnabled = true; BatchStartButton.IsEnabled = true; }
    }
    private void Failure(Exception ex) { Status.Text = ex is OperationCanceledException ? "已取消，可重新开始。" : "失败 · " + ex.Message; errorLog += ex + "\n"; }
    private async void Start(object sender, RoutedEventArgs e) => await Process("full");
    private async void Preview(object sender, RoutedEventArgs e) => await Process("preview");
    private async void Sample(object sender, RoutedEventArgs e) => await Process("sample");
    private void Cancel(object sender, RoutedEventArgs e) => cancellation?.Cancel();
    private void SaveLog(object sender, RoutedEventArgs e) { var d = new SaveFileDialog { FileName = "字幕工坊日志.txt", Filter = "文本|*.txt" }; if (d.ShowDialog() == true) { try { File.WriteAllText(d.FileName, engine.Log + errorLog); Status.Text = "日志已保存。"; } catch (Exception ex) { Failure(ex); } } }
    private void ChooseOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择视频输出文件夹" };
        if (Directory.Exists(Destination.Text)) dialog.InitialDirectory = Destination.Text;
        if (dialog.ShowDialog(this) == true)
        {
            Destination.Text = dialog.FolderName;
            Status.Text = "输出位置已设为 " + dialog.FolderName;
        }
    }
    private void OpenOutput(object sender, RoutedEventArgs e) { try { if (Directory.Exists(Destination.Text)) System.Diagnostics.Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Path.GetFullPath(Destination.Text) }, UseShellExecute = true }); } catch (Exception ex) { Failure(ex); } }
    private void ShowText(string title, string text) => new Window { Title = title, Owner = this, Width = 850, Height = 600, Content = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) } }.Show();
}
