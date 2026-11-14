# 字幕工坊 · Subtitle Studio

用于 Windows 11 x64 的中文桌面字幕与视频封装工具。文件在本地处理，支持拖入单个视频。
~~为拯救广大切片烤肉man和up主而出现！ありがとう、ChatGPTさん！~~

> 当前为验收候选版。主要处理流程和烧录专项测试已通过；干净 Windows 11 环境、真实磁盘耗尽等验收尚未完成。请先进行画面预览和 10 秒试输出。

## 三个功能

| 功能 | 输出 | 处理方式 |
|---|---|---|
| 无损换封装 | MKV / MP4 | 复制所选音视频流，不重新压缩 |
| 字幕内封 | MKV | 多份 ASS / SRT、名称、语言、默认轨道、TTF / OTF 字体附件 |
| 高画质字幕烧录 | MP4 | libass 渲染，H.264 CRF 18 / slow；AAC 复制，其他音频转 AAC |

支持轨道兼容性检查、字体引用与附件检查、UTF-8 / UTF-16 / GB18030、画面预览、10 秒试输出、进度、取消和日志。输出另存新文件，同名自动编号，临时文件通过检查后才成为正式文件。

## 使用

完整解压包含运行库和 `engines` 的 Windows 软件包，双击 `SubtitleStudio.exe`。无需安装开发环境。源码仓库本身不包含处理引擎和运行库。

1. 拖入 MKV / MP4，选择功能。
2. 选择保留轨道，添加字幕和字体；烧录时选择一条字幕和一条音轨。
3. 检查字幕文字与字体，选择输出目录。
4. 烧录建议先预览、试输出，再开始正式处理。

完整说明见 [使用说明](docs/使用说明.md)，当前测试范围见 [验收报告](docs/验收报告.md)。

### 处理边界

- **换封装保留音视频编码数据。** 容器、文件头、时间原点可能改变。AAC 前导和尾部裁剪在不同容器/播放器中的解释可能有差异，不能以整个文件哈希判断音画质量。
- **ASS / SRT 完整内封使用 MKV。** MP4 无法原样保存这些字幕和字体附件；程序要求取消不兼容轨道或换用 MKV。MP4 `mov_text` 转 MKV 也不默默转换格式。
- **烧录会重新编码画面。** 字幕成为画面的一部分。保留源分辨率与帧时间关系，首版仅支持 SDR；HDR、HLG、杜比视界会被拦截。
- ASS 播放效果依赖播放器对样式和字体的支持。系统安装字体并不代表字体已随 MKV 携带。
- 一次一个本地视频，不包含字幕编辑器、批处理或 B 站自动投稿。

## 从源码构建

需要 Windows x64 和 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```powershell
dotnet build SubtitleStudio/SubtitleStudio.csproj -c Release
dotnet publish SubtitleStudio/SubtitleStudio.csproj -c Release -r win-x64 --self-contained true -o publish
```

把以下程序放入 `publish/engines/`：

| 文件 | 已测试版本 | 来源 |
|---|---|---|
| ffmpeg.exe / ffprobe.exe | FFmpeg 9.0.2 essentials，含 libass / libx264 | [Gyan Windows 构建](https://www.gyan.dev/ffmpeg/builds/) |
| mkvmerge.exe / mkvextract.exe | MKVToolNix 103.0 x64 | [MKVToolNix](https://mkvtoolnix.download/downloads.html) |

保留组件许可证及版本信息，详见 [第三方组件](docs/第三方组件.txt)。所有进程通过 `ProcessStartInfo.ArgumentList` 启动，文件路径不经过 shell。

GitHub Actions 检查 Windows 编译与自包含发布，不代表完整视频验收。

## 开发与测试

- [agent.md](agent.md)：项目约定、模块职责、验证标准、发布检查。
- [tests/README.md](tests/README.md)：生成测试素材并运行集成测试。
- `MediaEngine.cs`：引擎进程、媒体分析、兼容性、字符编码。
- `JobRunner.cs`：任务检查、换封装、内封、烧录、预览及输出校验。
- `SubtitleInspector.cs`：字幕文字、ASS 字体引用、MKV 字体附件检查。
- `MainWindow.xaml` / `.cs`：WPF 界面及交互。

测试使用本地生成的色条/黑底视频和正弦音频，覆盖编码流复制、VFR、非零起点、ASS/SRT、预览一致性、默认音轨、字体、编码、取消和损坏输入。系统字体只在本机测试，不提交到仓库。

反馈问题时请附操作步骤、模式、源轨道信息和错误日志；日志包含本地路径，分享前请移除个人信息。不要公开上传未获授权的视频或字体。

## 许可证

本项目应用源码采用 [MIT](LICENSE)。FFmpeg、MKVToolNix、.NET 与字体等第三方组件适用各自许可证，MIT 不覆盖这些组件。仓库不包含第三方二进制或系统字体。
