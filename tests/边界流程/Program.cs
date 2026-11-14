// Expanded integration checks use real engines and independent stream probes.
using SubtitleStudio;
using System.Text;
using System.Text.Json;
var root = Path.GetFullPath(args[0]);
var engine = new MediaEngine(Path.GetFullPath(args[1]));
var runner = new JobRunner(engine);
var output = Path.Combine(root, "edge-output"); Directory.CreateDirectory(output);
var srt = new SubtitleSource { Path = Path.Combine(root,"fixtures","中文字幕.srt"), Name="中文", Default=true };
Job Make(MediaInfo m, Operation op, string container="mp4") => new(m,op,container,output,m.Tracks,[],[],srt,m.Tracks.FirstOrDefault(t=>t.Type=="audio"),"Microsoft YaHei",24,2,2);
async Task<string> Run(Job j,string kind="full") {var p=await runner.Execute(j,kind,default,(_,_)=>{}); Console.WriteLine("PASS " + Path.GetFileName(p)); return p;}
void Check(bool ok,string name) {if(!ok) throw new Exception(name); Console.WriteLine("PASS " + name);}
try {
 var nonzero=await engine.Probe(Path.Combine(root,"fixtures","nonzero.mkv"),default);
 Check(Math.Abs(nonzero.Duration-12)<0.05,"nonzero duration");
 var burned=await Run(Make(nonzero,Operation.Burn));
 Check((await engine.Probe(burned,default)).Tracks.First(t=>t.Type=="audio").Codec=="aac","FLAC -> AAC");
 var muxed=await Run(Make(nonzero,Operation.Mux,"mkv") with {Subtitles=new[]{srt},Fonts=new[]{@"C:\Windows\Fonts\AGENCYR.TTF"}});
 var muxInfo=await engine.Probe(muxed,default);
 Check(muxInfo.Tracks.Count(t=>t.Type=="attachment")==1,"font attachment retained");
 await Run(Make(muxInfo,Operation.Burn) with {BurnSubtitle=muxInfo.Tracks.First(t=>t.Type=="subtitle")},"preview");
 var vfr=await engine.Probe(Path.Combine(root,"fixtures","vfr.mp4"),default);
 var vfrBurn=await Run(Make(vfr,Operation.Burn));
 async Task<double[]> Times(string path) {
  using var d=JsonDocument.Parse(await engine.Run("ffprobe",["-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","json",path],default));
  return d.RootElement.GetProperty("frames").EnumerateArray().Select(f=>double.Parse(f.GetProperty("best_effort_timestamp_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
 }
 var a=await Times(vfr.Path); var b=await Times(vfrBurn);
 Check(a.Length==b.Length && a.Zip(b).All(x=>Math.Abs((x.First-a[0])-(x.Second-b[0]))<0.001),"VFR frame count and relative timestamps");
 var hdr=await engine.Probe(Path.Combine(root,"fixtures","hdr.mp4"),default);
 Check(hdr.IsHdr,"HDR detected");
 try {JobRunner.Validate(Make(hdr,Operation.Burn)); throw new Exception("HDR not blocked");} catch(IOException){Console.WriteLine("PASS HDR blocked");}
 Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
 var gb=Path.Combine(root,"fixtures","gb.srt"); File.WriteAllText(gb,File.ReadAllText(srt.Path),Encoding.GetEncoding(54936));
 Check(MediaEngine.ReadSubtitle(new(){Path=gb,Encoding="GB18030"}).Contains("中文字幕"),"GB18030");
 foreach(var enc in new[]{Encoding.Unicode,Encoding.BigEndianUnicode}) {var p=Path.Combine(root,"fixtures",enc.CodePage+".srt");File.WriteAllText(p,File.ReadAllText(srt.Path),enc);Check(MediaEngine.ReadSubtitle(new(){Path=p}).Contains("中文字幕"),enc.WebName);}
 using(var cts=new CancellationTokenSource()) {
  var before=Directory.GetFiles(output).Length;
  try {await runner.Execute(Make(vfr,Operation.Burn),"full",cts.Token,(phase,p)=>{if(phase=="处理")cts.Cancel();});throw new Exception("Cancel ignored");}
  catch(OperationCanceledException){Check(Directory.GetFiles(output).Length==before && !Directory.GetDirectories(output,".subtitle-studio-*").Any(),"cancel leaves no final or temp files");}
 }
 await Run(Make(vfr,Operation.Burn),"sample");
 var bad=Path.Combine(root,"fixtures","broken.mp4"); File.WriteAllText(bad,"not video");
 try {await engine.Probe(bad,default);throw new Exception("Damaged accepted");}catch(IOException){Console.WriteLine("PASS damaged input rejected");}
 Console.WriteLine("ALL EDGE CHECKS PASSED");
}finally{File.WriteAllText(Path.Combine(output,"engine-log.txt"),engine.Log);}
