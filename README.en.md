# 字幕工坊 · Subtitle Studio

[简体中文](README.md) | [English](README.en.md) | [日本語](README.ja.md)

A desktop tool with a Chinese interface for remuxing videos and working with subtitles on Windows 11 x64. All media processing happens locally. Configure individual videos or process multiple videos in a sequential queue.
~~Here to help clip translators and video creators! Thank you, ChatGPT!~~

> This is a release candidate for acceptance testing. The main processing workflows and dedicated subtitle burn-in tests have passed. Testing on a clean Windows 11 installation and under actual disk exhaustion remains incomplete. Preview a frame and generate a 10-second sample before a full burn-in.

## Three modes

| Mode | Output | Processing |
|---|---|---|
| Lossless remuxing | MKV / MP4 | Copies selected video and audio streams without re-encoding |
| Soft subtitle muxing | MKV | Multiple ASS / SRT files, track names, languages, default track, and TTF / OTF font attachments |
| High-quality subtitle burn-in | MP4 | Renders with libass; H.264 CRF 18 / slow; copies AAC audio and converts other audio codecs to AAC |

Includes track compatibility checks, subtitle font and attachment checks, UTF-8 / UTF-16 / GB18030 support, frame previews, 10-second samples, progress reporting, cancellation, and logs. Outputs are saved as new files with automatic numbering for name conflicts. Temporary outputs are checked before being promoted to final files.

## Usage

Fully extract the Windows package, including the runtime and `engines` folder, then run `SubtitleStudio.exe`. No development environment is required. The source repository does not include the processing engines or runtime.

The application interface is currently in Chinese. Chinese button labels below help you locate the corresponding controls; this README translation does not change the interface language.

1. Drag in an MKV / MP4 file and select a mode.
2. Select the tracks to keep, then add subtitles and fonts. For burn-in, select one subtitle and one audio track.
3. Check the subtitle text and fonts, and choose an output folder.
4. For burn-in, preview a frame and create a short sample before starting the full job.

See the [user guide](docs/使用说明.md) and [acceptance test report](docs/验收报告.md), both currently in Chinese.

### Remembered settings and batch processing

The application saves the mode, output format, output folder, and SRT font, size, and position when you close it normally, start processing, or add jobs to the queue. Settings are stored in `%LOCALAPPDATA%/SubtitleStudio/settings.json` and restored on startup. Corrupt settings fall back to defaults. Source files, track selections, subtitle/font file lists, and the queue are not restored across sessions.

- Configure a video and click **当前配置加入队列** (Add current configuration to queue). Each job keeps its own settings. Remuxing, muxing, and burn-in jobs can share a queue.
- Select a mode and output folder, then click **批量选择视频…** (Select multiple videos) or drag in multiple videos. Batch import keeps all source tracks and uses the default audio track, currently added font attachments, and SRT style settings. Incompatible tracks are reported; configure that file individually and deselect the incompatible track before adding it.
- Muxing matches ASS/SRT files in the same folder with the same base name: for example, `Episode01.mp4` matches `Episode01.ass` and `Episode01.srt`. Burn-in requires exactly one matching external subtitle; if none exists, it uses the sole embedded subtitle track. Ambiguous matches require individual configuration. Automatic import recognizes UTF-8/UTF-16; configure GB18030 manually.
- Use **查看任务设置** (View job settings) to review a selected job, then **开始队列 / 重试未完成** (Start queue / Retry unfinished). A failed job does not stop subsequent jobs. Cancel stops the queue; retry skips completed jobs. Outputs remain automatically numbered and never overwrite source files.
- Changing the interface settings does not modify jobs already queued. Remove and re-add a job to change its configuration. The queue is not saved on exit, and interrupted encoding cannot resume from a checkpoint.

### Processing limits

- **Remuxing preserves encoded video and audio data.** Containers, headers, and timeline origins may change. AAC priming and end trimming may be interpreted differently by containers and players; whole-file hashes cannot establish audio/video quality equivalence.
- **Use MKV to mux ASS/SRT without converting them.** MP4 cannot preserve these subtitles and font attachments as-is. Deselect incompatible tracks or switch to MKV. MP4 `mov_text` subtitles are not silently converted when remuxing to MKV either.
- **Burn-in re-encodes the video.** Subtitles become part of the image. Source resolution and frame timing are preserved. This version supports SDR burn-in only; HDR, HLG, and Dolby Vision inputs are blocked from burn-in.
- ASS playback depends on the player's support for styles and fonts. A font installed on the computer is not necessarily attached to the MKV file.
- The batch queue processes local videos one at a time. A subtitle editor and automatic Bilibili uploads are not included.

## Build from source

Requires Windows x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build SubtitleStudio/SubtitleStudio.csproj -c Release
dotnet publish SubtitleStudio/SubtitleStudio.csproj -c Release -r win-x64 --self-contained true -o publish
```

Place these programs in `publish/engines/`:

| Files | Tested version | Source |
|---|---|---|
| ffmpeg.exe / ffprobe.exe | FFmpeg 9.0.2 essentials, including libass / libx264 | [Gyan Windows builds](https://www.gyan.dev/ffmpeg/builds/) |
| mkvmerge.exe / mkvextract.exe | MKVToolNix 103.0 x64 | [MKVToolNix](https://mkvtoolnix.download/downloads.html) |

Keep component licenses and version information. See [third-party components](docs/第三方组件.txt) (Chinese). Processes are launched using `ProcessStartInfo.ArgumentList`; paths are not passed through a shell.

GitHub Actions checks Windows builds and self-contained publishing. It does not constitute full media acceptance testing.

## Development and testing

- [agent.md](agent.md): project conventions, module responsibilities, validation requirements, and release checks (Chinese).
- [tests/README.md](tests/README.md): generating fixtures and running integration tests (Chinese).
- `MediaEngine.cs`: engine processes, media probing, compatibility, and text encoding.
- `JobRunner.cs`: job validation, remuxing, muxing, burn-in, previews, and output validation.
- `SubtitleInspector.cs`: subtitle text, ASS font references, and MKV font attachments.
- `MainWindow.xaml` / `.cs`: WPF interface and interaction.

Tests use locally generated color bars, black video, and sine-wave audio. They cover encoded stream copying, VFR, nonzero start times, ASS/SRT, preview consistency, default audio tracks, fonts, text encoding, cancellation, and damaged input. System fonts are used only for local testing and are not committed to the repository.

When reporting an issue, include reproduction steps, the processing mode, source track details, and error logs. Logs contain local paths; remove personal information before sharing them. Do not publicly upload videos or fonts without authorization.

## License

Application source code is licensed under [MIT](LICENSE). FFmpeg, MKVToolNix, .NET, fonts, and other third-party components retain their own licenses; MIT does not cover them. The repository does not include third-party binaries or system fonts.
