# 集成测试

要求 Windows、.NET 10 SDK、带 libass/libx264 的 FFmpeg/FFprobe、MKVToolNix。先按根目录 README 发布到 `publish` 并将引擎放进 `publish/engines`。

在仓库根目录执行：

```powershell
./tests/生成素材.ps1 -Engines ./publish/engines
dotnet run --project tests/基础流程 -- ./tests ./publish/engines
dotnet run --project tests/边界流程 -- ./tests ./publish/engines
dotnet run --project tests/烧录验证 -- ./tests ./publish/engines
dotnet run --project tests/字体检查 -- ./tests ./publish/engines
dotnet run --project tests/补充验证 -- ./tests ./publish/engines C:/Windows/Fonts/MiSans-Regular.otf
```

边界测试及字体检查当前使用本机 `C:/Windows/Fonts/AGENCYR.TTF`（Agency FB）作为附件。如本机没有此字体，请调整边界测试的字体路径，并相应调整字体检查中的字体家族名。补充验证允许通过第三个参数提供本机 OTF 文件。字体不随仓库提供。

测试项目直接编译应用的处理模块，生成色条/黑底视频和正弦音频，不使用用户视频。测试结果写入各 `*-output` 目录及技术日志，这些文件被 git 忽略。

- 基础流程：双向换封装、音视频编码数据哈希、多字幕内封、ASS 预览/试输出/完整烧录。
- 边界流程：非零起点、FLAC 转 AAC、字体附件、VFR 逐帧时间戳、HDR 拦截、文字编码、取消及损坏输入。
- 烧录验证：字幕出现区间、中文换行与位置、重叠和卡拉 OK 事件、预览与正式结果逐帧比较、试输出对应首帧、音轨选择和复制、全文件解码。
- 字体检查：已有字幕文字、字体附件名称、缺失字体、行内字体引用。
- 补充验证：OTF 附件封装与烧录预览、HLG 实际媒体识别、引擎失败后清理。

这些测试不替代独立播放器人工检查、真实磁盘耗尽和干净 Windows 环境验收。

### 杜比视界回归样本

可从 FFmpeg 官方 FATE 下载小型样本，在仓库根目录执行以下命令。

```powershell
New-Item -ItemType Directory -Force tests/fixtures/dovi | Out-Null
foreach ($name in @('dovi-p5.mp4', 'dovi-p7.mp4', 'dovi-p81.mp4', 'dovi-p7-hvce.mp4')) {
    Invoke-WebRequest ('https://fate-suite.ffmpeg.org/mov/' + $name) -OutFile ('tests/fixtures/dovi/' + $name)
}
dotnet run --project tests/杜比视界 -- tests/fixtures/dovi publish/engines
```

样本和生成文件被 git 忽略。校验值见 docs/验收报告.md。该项目验证实际 MP4 文件中杜比视界配置的识别及烧录前拦截。

### 批量与设置

```powershell
dotnet run --project tests/批量与设置 -- tests/fixtures publish/engines tests/batch-output
```

覆盖设置保存/恢复/损坏回退、任务快照隔离、轨道身份映射、同名字幕及歧义、HDR 和容器不兼容拦截、真实三种任务输出与严格解码、失败继续、取消后保留待处理任务、重试跳过已完成任务。

### 旋转与所选轨道时间范围

```powershell
dotnet run --project tests/媒体校验 -- publish/engines tests/media-validation-output
```

自动生成 3 秒视频、3/7 秒音轨、结束于第 6 秒的字幕，以及带 90°/180°/270° 显示矩阵的视频。验证取消长音轨后换封装、长字幕内封、旋转素材的预览/试输出/正式烧录、输出尺寸和严格解码，以及文件名后缀长度预留。
