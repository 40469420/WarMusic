using System.IO;
using System.Text.Json;
using WarMusic.Models;
namespace WarMusic.Services;
public static class Store
{
 public static string Root {get;private set;}="";
 public static string Data=>System.IO.Path.Combine(Root,"data");
 public static string? RecoveryMessage {get;private set;}
 static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
 public static void Initialize(string? root=null)
 {
  if(root!=null)Root=root;
  else {var dir=new DirectoryInfo(AppContext.BaseDirectory);while(dir.Parent!=null&&!File.Exists(System.IO.Path.Combine(dir.FullName,"WarMusic.root")))dir=dir.Parent;Root=File.Exists(System.IO.Path.Combine(dir.FullName,"WarMusic.root"))?dir.FullName:AppContext.BaseDirectory;}
  Directory.CreateDirectory(Data);Directory.CreateDirectory(System.IO.Path.Combine(Data,"library"));
 }
 public static Settings Load()
 {
  var path=System.IO.Path.Combine(Data,"settings.json");if(!File.Exists(path))return new();
  try {var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))??throw new InvalidDataException("Empty settings");if(s.Profiles==null||s.Profiles.Count==0||s.Sounds==null)throw new InvalidDataException("Incomplete settings");foreach(var p in s.Profiles)Sanitize(p);return s;}
  catch(Exception ex){RecoveryMessage="Settings were damaged. Defaults loaded; the original was preserved.";File.Copy(path,path+".corrupt-"+DateTime.Now.ToString("yyyyMMddHHmmss"),true);Log(ex.Message);return new();}
 }
 public static void Sanitize(Profile p)
 {
  p.MicGain=Bound(p.MicGain,0,2,1);p.AppGain=Bound(p.AppGain,0,16,4);p.ClipGain=Bound(p.ClipGain,0,2,.65f);p.SendGain=Bound(p.SendGain,0,1,1);p.MonitorGain=Bound(p.MonitorGain,0,1,.65f);
  p.ThresholdDb=Bound(p.ThresholdDb,-65,-10,-35);p.ReductionDb=Bound(p.ReductionDb,0,40,15);p.AttackMs=Bound(p.AttackMs,1,500,30);p.HoldMs=Bound(p.HoldMs,0,2000,350);p.ReleaseMs=Bound(p.ReleaseMs,10,3000,600);p.DuckMode=Math.Clamp(p.DuckMode,0,2);p.TransmitMode=Math.Clamp(p.TransmitMode,0,1);
 }
 static float Bound(float v,float min,float max,float fallback)=>float.IsFinite(v)?Math.Clamp(v,min,max):fallback;
 public static void Save(Settings settings){var p=System.IO.Path.Combine(Data,"settings.json");File.WriteAllText(p+".tmp",JsonSerializer.Serialize(settings,Options));File.Move(p+".tmp",p,true);}
 public static void Log(string text){try{var path=System.IO.Path.Combine(Data,"diagnostics.log");if(File.Exists(path)&&new FileInfo(path).Length>1_000_000)File.Move(path,path+".old",true);File.AppendAllText(path,$"{DateTime.Now:O} {text}{Environment.NewLine}");}catch{}}
}

