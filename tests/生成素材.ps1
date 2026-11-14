param([Parameter(Mandatory=$true)][string]$Engines)
$ErrorActionPreference='Stop'
$ff=Join-Path (Resolve-Path $Engines) 'ffmpeg.exe'
$fixtures=Join-Path $PSScriptRoot 'fixtures'
function Run-FF([string[]]$Arguments) { & $ff @Arguments; if($LASTEXITCODE -ne 0){throw '生成测试视频失败'} }
Run-FF @('-nostdin','-v','error','-f','lavfi','-i','testsrc2=size=640x360:rate=24:duration=12','-f','lavfi','-i','sine=frequency=440:duration=12','-f','lavfi','-i','sine=frequency=660:duration=12','-map','0:v','-map','1:a','-map','2:a','-c:v','libx264','-preset','ultrafast','-c:a','aac','-disposition:a:0','0','-disposition:a:1','default','-y',(Join-Path $fixtures '中文 空格 & 视频.mp4'))
Run-FF @('-nostdin','-v','error','-i',(Join-Path $fixtures '中文 空格 & 视频.mp4'),'-map','0:v','-map','0:a:1','-c:v','copy','-c:a','flac','-output_ts_offset','5','-y',(Join-Path $fixtures 'nonzero.mkv'))
Run-FF @('-nostdin','-v','error','-f','lavfi','-i','testsrc2=size=640x360:rate=30:duration=12','-vf','select=if(lt(t\,6)\,not(mod(n\,2))\,1)','-fps_mode','vfr','-c:v','libx264','-preset','ultrafast','-y',(Join-Path $fixtures 'vfr.mp4'))
Run-FF @('-nostdin','-v','error','-i',(Join-Path $fixtures 'vfr.mp4'),'-vf','setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc','-c:v','libx264','-preset','ultrafast','-x264-params','colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc','-y',(Join-Path $fixtures 'hdr.mp4'))
