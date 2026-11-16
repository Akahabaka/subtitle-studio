using SubtitleStudio;
using System.Text.Json;
var root=Path.GetFullPath(args[0]); var e=new MediaEngine(Path.GetFullPath(args[1])); var runner=new JobRunner(e);
var output=Path.Combine(root,"extra-output");Directory.CreateDirectory(output);
var media=await e.Probe(Path.Combine(root,"fixtures","中文 空格 & 视频.mp4"),default);
var srt=new SubtitleSource{Path=Path.Combine(root,"fixtures","中文字幕.srt"),Name="中文"};
Job Make()=>new(media,Operation.Burn,"mp4",output,media.Tracks,[],[],srt,media.Tracks.First(t=>t.Type=="audio"&&t.Default),"MiSans",24,2,1);
try{
 var font=args.Length>2?Path.GetFullPath(args[2]):@"C:\Windows\Fonts\MiSans-Regular.otf";
 if(!File.Exists(font))throw new Exception("请提供本机 OTF 字体路径作为第三个参数");
 var mux=await runner.Execute(Make() with{Operation=Operation.Mux,Container="mkv",Subtitles=new[]{srt},Fonts=new[]{font}},"full",default,(_,_)=>{});
 var mi=await e.Probe(mux,default);
 var preview=await runner.Execute(Make() with{Media=mi,Tracks=mi.Tracks,BurnSubtitle=mi.Tracks.First(t=>t.Type=="subtitle"),Audio=mi.Tracks.First(t=>t.Type=="audio"&&t.Default)},"preview",default,(_,_)=>{});
 if(new FileInfo(preview).Length<1000)throw new Exception("OTF preview empty");
 Console.WriteLine("PASS OTF 附件封装、提取、烧录预览");
 var hlg=Path.Combine(output,"hlg.mp4");
 await e.Run("ffmpeg",["-v","error","-i",media.Path,"-t","1","-an","-vf","setparams=color_primaries=bt2020:color_trc=arib-std-b67:colorspace=bt2020nc","-c:v","libx264","-preset","ultrafast","-x264-params","colorprim=bt2020:transfer=arib-std-b67:colormatrix=bt2020nc","-y",hlg],default);
 var h=await e.Probe(hlg,default);if(!h.IsHdr)throw new Exception("HLG detection failed");
 try{JobRunner.Validate(Make() with{Media=h,Tracks=h.Tracks});throw new Exception("HLG accepted");}catch(IOException){Console.WriteLine("PASS HLG 实际媒体识别与确认前拦截");}
 JobRunner.Validate(Make() with {Media=h,Tracks=h.Tracks,HdrRiskAccepted=true});
 var before=Directory.GetFiles(output).Length;
 try{await runner.Execute(Make() with{Operation=Operation.Mux,Container="mkv",Subtitles=new[]{srt},Fonts=new[]{Path.Combine(output,"missing.ttf")}},"full",default,(_,_)=>{});throw new Exception("Engine error ignored");}catch(IOException){if(Directory.GetFiles(output).Length!=before||Directory.GetDirectories(output,".subtitle-studio-*").Any())throw new Exception("Failure cleanup failed");Console.WriteLine("PASS 引擎失败后无正式输出和临时残留");}
}finally{File.WriteAllText(Path.Combine(output,"engine-log.txt"),e.Log);}
