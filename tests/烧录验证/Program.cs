using SubtitleStudio;
using System.Globalization;
using System.Text.Json;
var root=Path.GetFullPath(args[0]); var engine=new MediaEngine(Path.GetFullPath(args[1])); var runner=new JobRunner(engine);
var output=Path.Combine(root,"burn-output"); Directory.CreateDirectory(output);
void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
async Task<string> Run(Job j,string kind="full")=>await runner.Execute(j,kind,default,(_,_)=>{});
async Task<byte[]> Frame(string path,double seconds,string name){var raw=Path.Combine(output,name+".rgb");await engine.Run("ffmpeg",["-v","error","-i",path,"-ss",seconds.ToString(CultureInfo.InvariantCulture),"-frames:v","1","-pix_fmt","rgb24","-f","rawvideo","-y",raw],default);return File.ReadAllBytes(raw);}
int Pixels(byte[] image,int minY=0,int maxY=360){int n=0;for(int y=minY;y<maxY;y++)for(int x=0;x<640;x++){int i=(y*640+x)*3;if(Math.Max(image[i],Math.Max(image[i+1],image[i+2]))>70)n++;}return n;}
double Mae(byte[] a,byte[] b)=>a.Length==b.Length?a.Zip(b).Average(x=>Math.Abs((int)x.First-x.Second)):double.PositiveInfinity;
try{
 var sourcePath=Path.Combine(output,"black.mp4");
 await engine.Run("ffmpeg",["-nostdin","-v","error","-f","lavfi","-i","color=c=black:s=640x360:r=24:d=12","-i",Path.Combine(root,"fixtures","中文 空格 & 视频.mp4"),"-map","0:v","-map","1:a","-c:v","libx264","-preset","ultrafast","-c:a","copy","-disposition:a:0","0","-disposition:a:1","default","-y",sourcePath],default);
 var media=await engine.Probe(sourcePath,default);
 var assPath=Path.Combine(output,"效果.ass");
 File.WriteAllText(assPath,File.ReadAllText(Path.Combine(root,"fixtures","样式.ass"))+"\nDialogue: 0,0:00:10.00,0:00:11.80,Default,,0,0,0,,结尾字幕\n");
 var ass=new SubtitleSource{Path=assPath,Name="ASS"};var srt=new SubtitleSource{Path=Path.Combine(root,"fixtures","中文字幕.srt"),Name="SRT"};
 Job Make(object sub)=>new(media,Operation.Burn,"mp4",output,media.Tracks,[],[],sub,media.Tracks.First(t=>t.Type=="audio"&&t.Default),"Microsoft YaHei",24,2,2);
 var job=Make(ass); var full=await Run(job);var sample=await Run(job,"sample");
 var before=await Frame(full,0.2,"before");var first=await Frame(full,0.75,"first");var overlap=await Frame(full,1.5,"overlap");var karaoke=await Frame(full,4.25,"karaoke");var after=await Frame(full,8.5,"after");var end=await Frame(full,11,"end");
 Check(Pixels(before)==0 && Pixels(after)==0,"字幕区间外没有烧入文字");
 Check(Pixels(first,240)>500 && Pixels(first,0,180)==0,"ASS 中文换行与底部位置");
 Check(Pixels(overlap,0,180)>200 && Pixels(overlap,240)>500,"ASS 定位与重叠事件");
 Check(Pixels(karaoke)>100 && Pixels(end)>200,"ASS 卡拉 OK 和结尾事件存在");
 foreach(var time in new[]{0.75,1.5,4.25,5.0,11.0}){
  var preview=await Run(job with{Position=time},"preview"); var p=await Frame(preview,0,"preview-"+time);var f=await Frame(full,time,"full-"+time);
  Check(Mae(p,f)<1.5,"预览与正式烧录画面一致 @"+time);
 }
 var fullAt2=await Frame(full,2,"full2");var sample0=await Frame(sample,0,"sample0");Check(Mae(fullAt2,sample0)<1.5,"试输出首帧与正式输出相同时间一致");
 async Task<string> AudioHash(string path,int index)=>await engine.Run("ffmpeg",["-v","error","-i",path,"-map","0:a:"+index,"-c","copy","-f","hash","-hash","sha256","-"],default);
 Check(await AudioHash(media.Path,1)==await AudioHash(full,0),"选择默认第二音轨并原样复制 AAC");
 var srtFull=await Run(Make(srt) with{Alignment=8,FontSize=30,Audio=media.Tracks.First(t=>t.Type=="audio")});
 var srtFrame=await Frame(srtFull,1,"srt-top");Check(Pixels(srtFrame,0,180)>500&&Pixels(srtFrame,240)==0,"SRT 字号与顶部位置生效");
 Check(await AudioHash(media.Path,0)==await AudioHash(srtFull,0),"手动切换第一音轨并原样复制 AAC");
 var mux=await Run(job with{Operation=Operation.Mux,Container="mkv",Subtitles=new[]{ass}});
 var internalMedia=await engine.Probe(mux,default);
 var internalBurn=await Run(job with{Media=internalMedia,Tracks=internalMedia.Tracks,BurnSubtitle=internalMedia.Tracks.First(t=>t.Type=="subtitle"),Audio=internalMedia.Tracks.First(t=>t.Type=="audio"&&t.Default)});
 var internalFrame=await Frame(internalBurn,1.5,"internal");Check(Pixels(internalFrame,0,180)>200&&Pixels(internalFrame,240)>500,"内封 ASS 烧录样式和位置");
 foreach(var p in new[]{full,sample,srtFull,internalBurn}) await engine.Run("ffmpeg",["-v","error","-xerror","-i",p,"-f","null","-"],default);
 Console.WriteLine("ALL BURN CHECKS PASSED");
 File.WriteAllText(Path.Combine(output,"results.txt"),"PASS 全部烧录专项测试\n正式 ASS："+full+"\n试输出："+sample+"\nSRT："+srtFull+"\n内封字幕："+internalBurn);
}finally{File.WriteAllText(Path.Combine(output,"engine-log.txt"),engine.Log);}
