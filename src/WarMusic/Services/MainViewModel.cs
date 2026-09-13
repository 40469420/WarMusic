using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using WarMusic.Audio;
using WarMusic.Models;
namespace WarMusic.Services;
public sealed class ActionCommand(Action<object?> action):ICommand
{
 public bool CanExecute(object? p)=>true;public void Execute(object? p)=>action(p);public event EventHandler? CanExecuteChanged {add{} remove{}}
}
public sealed partial class MainViewModel:Observable,IDisposable
{
 public AudioEngine Engine {get;}
 readonly Settings settings;readonly DispatcherTimer timer;Hotkeys? hotkeys;bool disposed,connecting;int tick;

 bool seeking;
 public ObservableCollection<Sound> Sounds {get;}
 public ObservableCollection<Sound> Queue {get;}=[];
 public ObservableCollection<Profile> Profiles {get;}
 public ObservableCollection<string> Collections {get;}=[];
 public ObservableCollection<Device> Microphones {get;}=[];public ObservableCollection<Device> Outputs {get;}=[];public ObservableCollection<AppSource> Applications {get;}=[];
 public ICollectionView Library {get;}
 Profile profile;public Profile Profile {get=>profile;set{if(value==null||value==profile)return;StopRecovery();Engine.Stop();profile=value;settings.ActiveProfile=value.Name;Changed();Changed(nameof(SelectedMic));Changed(nameof(SelectedMonitor));Changed(nameof(SelectedCable));Collection=value.Collection;RegisterHotkeys();Save();}}
 public Device? SelectedMic {get=>Microphones.FirstOrDefault(x=>x.Id==Profile.MicId);set{if(value!=null&&value.Id!=Profile.MicId){StopRecovery();Engine.Stop();Profile.MicId=value.Id;}Changed();}}
 public Device? SelectedMonitor {get=>Outputs.FirstOrDefault(x=>x.Id==Profile.MonitorId);set{if(value!=null&&value.Id!=Profile.MonitorId){StopRecovery();Engine.Stop();Profile.MonitorId=value.Id;}Changed();}}
 public Device? SelectedCable {get=>Outputs.FirstOrDefault(x=>x.Id==Profile.CableId);set{if(value!=null&&value.Id!=Profile.CableId){StopRecovery();Engine.Stop();Profile.CableId=value.Id;}Changed();}}
 AppSource? selectedApp;public AppSource? SelectedApp {get=>selectedApp;set{Set(ref selectedApp,value);if(value!=null)Profile.SourceName=value.Name;}}
 Sound? selectedSound;public Sound? SelectedSound {get=>selectedSound;set=>Set(ref selectedSound,value);}
 Sound? selectedQueued;public Sound? SelectedQueued {get=>selectedQueued;set=>Set(ref selectedQueued,value);}
 string search="";public string Search {get=>search;set{Set(ref search,value);Library.Refresh();}}
 string collection="All sounds";public string Collection {get=>collection;set{Set(ref collection,value??"All sounds");Profile.Collection=collection;Library.Refresh();}}
 bool favorites;public bool Favorites {get=>favorites;set{Set(ref favorites,value);Library.Refresh();}}
 bool repeat;public bool Repeat {get=>repeat;set{Set(ref repeat,value);Engine.SetLoop(value);}}
 string notice="Choose your devices in Setup, then test the route.";public string Notice {get=>notice;set=>Set(ref notice,value);}
 string profileName="";public string ProfileName {get=>profileName;set=>Set(ref profileName,value);}
 string newCollection="";public string NewCollection {get=>newCollection;set=>Set(ref newCollection,value);}
 string sourceState="No application connected";public string SourceState {get=>sourceState;set=>Set(ref sourceState,value);}
 string hotkeyState="";public string HotkeyState {get=>hotkeyState;set=>Set(ref hotkeyState,value);}
 bool busy;public bool Busy {get=>busy;set=>Set(ref busy,value);}
 int tab;public int Tab {get=>tab;set=>Set(ref tab,value);}
 public bool IsRouting=>Engine.Routing;
 public string SessionLabel=>!Engine.Routing?"Offline":Engine.Transmitting?"Broadcasting":"Voice connected";
 public string SessionColor=>Engine.Routing?"#AABA85":"#59634F";
 public string Transmission=>!Engine.Routing?"ROUTING UNAVAILABLE":Engine.Transmitting?"MUSIC ROUTED TO GAME INPUT":"MUSIC OFF · VOICE ROUTED";
 public string MusicButtonLabel=>Engine.Transmitting?"Mute music":"Enable music";
 public string Listening=>Engine.IsPreview?"PRIVATE PREVIEW":"";
 public bool LocalTransport=>Engine.HasLocalTrack;
 public string Track=>Engine.PlayingName;
 public string TrackDetails=>LocalTransport?(Engine.IsPreview?"Private preview · local sound":"Local soundboard"):"Local soundboard";
 public string PlayLabel=>Engine.IsPaused?"Resume":"Pause";
 public bool CanTogglePlayback=>LocalTransport;
 public bool CanSeek=>LocalTransport;
 public string TimeLabel=>$"{Engine.Position:mm\\:ss} / {Engine.Duration:mm\\:ss}";
 public double Duration=>Math.Max(1,Engine.Duration.TotalSeconds);
 public double Position=>Engine.Position.TotalSeconds;
 public bool LiveMic {get=>Engine.LiveMicrophone;set{Safe(()=>{Engine.SetLiveMicrophone(Profile,value);Notice=value?"Live mic is playing in your headphones. Use headphones to avoid feedback; nothing is being recorded.":"Live microphone listening stopped.";});Changed();}}
 float liveMicLevel=.5f;public float LiveMicLevel {get=>liveMicLevel;set=>Set(ref liveMicLevel,value);}
 public string LiveMicStatus=>Engine.LiveMicrophone?"LIVE · headphones only · not recording":"Live listening is off";
 public double MicMeter=>Engine.Running?Meter(Engine.MicPeak):0;public double MusicMeter=>Engine.Running?Meter(Engine.AppPeak):0;public double OutputMeter=>Engine.Running?Meter(Engine.OutputPeak):0;
 public double MusicBoost {get=>Math.Clamp(20*Math.Log10(Math.Max(.0631,Profile.AppGain)),-24,24);set{Profile.AppGain=(float)Math.Pow(10,value/20);Changed();Changed(nameof(BoostLabel));}}
 public string BoostLabel=>$"{Profile.AppGain:0.#}×";
 public double GameMusicVolume {get=>Profile.SendGain*100;set{Profile.SendGain=(float)value/100;Changed();Changed(nameof(GameMusicLabel));}}
 public string GameMusicLabel=>$"{Profile.SendGain*100:0}%";
 public string MicLabel=>$"{Profile.MicGain*100:0}%";
 public string ClipLabel=>$"{Profile.ClipGain*100:0}%";
 public string MonitorLabel=>$"{Profile.MonitorGain*100:0}%";
 double musicPower;float musicPeak;
 public double SentMusicMeter=>Engine.Routing?Meter(musicPeak):0;
 public string MusicLevelStatus=>!Engine.Routing?"Start routing to check music":!Engine.Transmitting?"Music muted · voice stays on":musicPower<1e-9?"Waiting for music":Engine.DuckGain<.8?"Ducked · music lowered for voice":Engine.Limited?"Too loud · lower boost":10*Math.Log10(musicPower)<-35?"Quiet · raise Music boost":10*Math.Log10(musicPower)>-12?"Loud · lower boost if distorted":"Good music level";
 public string MusicLevelColor=>!Engine.Transmitting||musicPower<1e-9?"#A0AC94":Engine.Limited||10*Math.Log10(musicPower)>-12?"#EF9B81":10*Math.Log10(musicPower)<-35?"#E8C777":"#B8CE79"; public string DuckStatus=>Engine.DuckGain<.95?$"Ducking · {20*Math.Log10(Math.Max(.0001,Engine.DuckGain)):0} dB":"Music at full level";
 public string Limiter=>Engine.Running&&Engine.Limited?"Peak limiter active":"Peak limiter ready";
 public string TestStatus=>Engine.Recording?"Recording test · up to 10 seconds":Engine.HasRecording?"Test ready · private playback only":"No test recording";
 public string RouteStatus=>Engine.Routing?"Microphone route active":Engine.LiveMicrophone?"Live mic test · headphones only":Engine.Running?"Headphones only":"Audio stopped";
 public string LibraryCount=>$"{Sounds.Count} sounds";
 static double Meter(float peak)=>Math.Clamp((20*Math.Log10(Math.Max(peak,.001))+60)/60*100,0,100);
 public ICommand RefreshCommand {get;}public ICommand RouteCommand {get;}public ICommand PreviewStartCommand {get;}public ICommand StopRouteCommand {get;}public ICommand ConnectCommand {get;}public ICommand DisconnectCommand {get;}
 public ICommand ImportCommand {get;}public ICommand PlayCommand {get;}public ICommand PreviewCommand {get;}public ICommand PauseCommand {get;}public ICommand StopCommand {get;}public ICommand PanicCommand {get;}public ICommand ToggleCommand {get;}public ICommand QueueCommand {get;}public ICommand NextCommand {get;}public ICommand RemoveQueueCommand {get;}
 public ICommand TransportPauseCommand {get;}
 public ICommand FavoriteCommand {get;}public ICommand RemoveCommand {get;}public ICommand AssignCollectionCommand {get;}public ICommand SaveCommand {get;}public ICommand SaveProfileCommand {get;}public ICommand HotkeyCommand {get;}public ICommand RecordCommand {get;}public ICommand EndRecordCommand {get;}public ICommand PreviewTestCommand {get;}public ICommand DeleteTestCommand {get;}public ICommand HelpCommand {get;}public ICommand CableCommand {get;}public ICommand WindowsCommand {get;}
 public MainViewModel(AudioEngine? engine=null)
 {
  Engine=engine??new();settings=Store.Load();Sounds=new(settings.Sounds);Profiles=new(settings.Profiles);profile=Profiles.FirstOrDefault(p=>p.Name==settings.ActiveProfile)??Profiles[0];collection=profile.Collection;
  foreach(var p in Profiles)p.PropertyChanged+=ProfileChanged;
  Library=CollectionViewSource.GetDefaultView(Sounds);Library.Filter=x=>x is Sound s&&(!Favorites||s.Favorite)&&(Collection=="All sounds"||s.Collection==Collection)&&(string.IsNullOrWhiteSpace(Search)||s.Name.Contains(Search,StringComparison.OrdinalIgnoreCase));
  Library.SortDescriptions.Add(new(nameof(Sound.Name),ListSortDirection.Ascending));
  foreach(var s in Sounds)s.PropertyChanged+=SoundChanged;
  RebuildCollections();
  Engine.Error+=message=>Application.Current.Dispatcher.BeginInvoke(()=>{Notice=message;Store.Log(message);if(!Engine.Routing)Engine.Stop();SourceState="Unavailable · reconnect in the source panel";});
  Engine.TrackEnded+=()=>Application.Current.Dispatcher.BeginInvoke(()=>{if(!Engine.IsPreview)PlayNext();});
  RefreshCommand=Cmd(Refresh);RouteCommand=Cmd(()=>{wantRoute=true;Save();Engine.Start(Profile,true);Notice="Route ready. Select the cable's recording endpoint as the microphone in your game. Music starts off.";});
  PreviewStartCommand=Cmd(()=>{StopRecovery();Save();Engine.Start(Profile,false);Notice="Headphones ready. Select a sound and use Private preview.";});StopRouteCommand=Cmd(()=>{StopRecovery();Engine.Stop();Notice="All audio stopped.";});
  ConnectCommand=new ActionCommand(async _=>await Connect());DisconnectCommand=Cmd(()=>{wantSource=false;suggestedGain=null;Changed(nameof(CanApplyLevel));Engine.DisconnectSource();SourceState="No application connected";});
  ImportCommand=new ActionCommand(async _=>{var d=new OpenFileDialog{Filter="Audio files|*.wav;*.mp3",Multiselect=true};if(d.ShowDialog()==true)await Import(d.FileNames);});
  PlayCommand=Cmd(()=>PlaySelected(false));PreviewCommand=Cmd(()=>PlaySelected(true));PauseCommand=Cmd(()=>Engine.Pause());StopCommand=Cmd(()=>Engine.StopLocal());PanicCommand=Cmd(()=>{Engine.Panic();Notice="Music cut. Your microphone remains available.";});ToggleCommand=Cmd(()=>{if(Measuring)return;if(Profile.TransmitMode==1){Notice="Hold mode: use your configured hold key.";return;}if(!Engine.Routing)throw new InvalidOperationException("Start routing in Setup first.");Engine.Toggle();});
  TransportPauseCommand=PauseCommand;
  QueueCommand=Cmd(()=>{if(SelectedSound!=null)Queue.Add(SelectedSound);});NextCommand=Cmd(PlayNext);RemoveQueueCommand=Cmd(()=>{if(SelectedQueued!=null)Queue.Remove(SelectedQueued);});
  FavoriteCommand=Cmd(()=>{if(SelectedSound!=null)SelectedSound.Favorite=!SelectedSound.Favorite;});
  RemoveCommand=Cmd(()=>{if(SelectedSound!=null){var s=SelectedSound;Sounds.Remove(s);s.PropertyChanged-=SoundChanged;RebuildCollections();Save();Changed(nameof(LibraryCount));}});
  AssignCollectionCommand=Cmd(()=>{if(SelectedSound!=null&&!string.IsNullOrWhiteSpace(NewCollection)){SelectedSound.Collection=NewCollection.Trim();RebuildCollections();Save();}});
  SaveCommand=Cmd(()=>{Save();Notice="Settings saved.";});SaveProfileCommand=Cmd(CreateProfile);HotkeyCommand=Cmd(()=>{RegisterHotkeys();Save();});
  RecordCommand=Cmd(()=>{Engine.BeginTest();Notice="Recording microphone and local sounds only. Application music keeps routing and is excluded from this recording.";});
  EndRecordCommand=Cmd(()=>Engine.EndTest());PreviewTestCommand=Cmd(()=>Engine.PreviewTest());DeleteTestCommand=Cmd(()=>{Engine.StopLocal();Engine.DeleteTest();var path=Path.Combine(Store.Data,"mix-test.wav");if(File.Exists(path))File.Delete(path);});
  HelpCommand=Cmd(()=>Open(Path.Combine(Store.Root,"docs","SETUP.md")));CableCommand=Cmd(()=>Open("https://vb-audio.com/Cable/"));WindowsCommand=Cmd(()=>Open("ms-settings:apps-volume"));
  InitializeFeatures();Refresh();Tab=string.IsNullOrEmpty(Profile.CableId)?1:0;if(Store.RecoveryMessage!=null)Notice=Store.RecoveryMessage;
  timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(33)};timer.Tick+=(_,_)=>Tick();timer.Start();

 }
 ICommand Cmd(Action action)=>new ActionCommand(_=>Safe(action));
 void Safe(Action action){try{action();}catch(Exception ex){Notice=ex.Message;Store.Log(ex.ToString());}}
 static void Open(string path)=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
 void SoundChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(Sound.Favorite))Library.Refresh();}
 void ProfileChanged(object? sender,PropertyChangedEventArgs e){if(sender==Profile&&e.PropertyName==nameof(Profile.TransmitMode))Engine.Panic();}
 void RebuildCollections(){var current=Collection;Collections.Clear();Collections.Add("All sounds");foreach(var c in Sounds.Select(s=>s.Collection).Distinct().Order())Collections.Add(c);Collection=Collections.Contains(current)?current:"All sounds";}
 void Refresh()
 {
  Safe(()=>{Microphones.Clear();foreach(var d in Devices.List(DataFlow.Capture))Microphones.Add(d);Outputs.Clear();foreach(var d in Devices.List(DataFlow.Render))Outputs.Add(d);
   if(string.IsNullOrEmpty(Profile.MonitorId)){using var e=new MMDeviceEnumerator();using var d=e.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);Profile.MonitorId=d.ID;}
   if(string.IsNullOrEmpty(Profile.MicId)){using var e=new MMDeviceEnumerator();using var d=e.GetDefaultAudioEndpoint(DataFlow.Capture,Role.Communications);Profile.MicId=d.ID;}
   Changed(nameof(SelectedMic));Changed(nameof(SelectedMonitor));Changed(nameof(SelectedCable));
   int previous=SelectedApp?.Pid??0;string preferred=Profile.SourceName;Applications.Clear();foreach(var a in Devices.Applications())Applications.Add(a);SelectedApp=Applications.FirstOrDefault(a=>a.Pid==previous)??Applications.FirstOrDefault(a=>a.Name==preferred);
  });
 }
 async Task Connect()
 {
  wantSource=true;if(connecting)return;connecting=true;suggestedGain=null;Changed(nameof(CanApplyLevel));
  try{if(SelectedApp==null)throw new InvalidOperationException("Choose an application. Start its playback and Refresh if it is missing.");SourceState="Connecting…";var source=Devices.ResolveApplication(SelectedApp);await Engine.ConnectSource(source.Pid);SourceState="Connected · waiting for playback";Notice="Application connected. Its original Windows playback remains audible; leave extra monitoring off to avoid an echo.";Save();}
  catch(Exception ex){Notice=ex.Message;SourceState="Unavailable · reconnect to retry";Store.Log(ex.ToString());}finally{connecting=false;}
 }
 public async Task Import(IEnumerable<string> paths)
 {
  if(Busy)return;Busy=true;int imported=0;var errors=new List<string>();
  try
  {
   foreach(string input in paths)
   {
    try
    {
     var sound=await Task.Run(()=>{string ext=Path.GetExtension(input).ToLowerInvariant();if(ext!=".mp3"&&ext!=".wav")throw new InvalidOperationException("Only WAV and MP3 files are supported.");string relative=Path.Combine("data","library",Guid.NewGuid().ToString("N")+ext);string target=Path.Combine(Store.Root,relative);File.Copy(input,target);try{using var reader=new AudioFileReader(target);double sum=0;long n=0;float peak=0;float[] samples=new float[16384];int read;while((read=reader.Read(samples,0,samples.Length))>0){for(int i=0;i<read;i++){float v=samples[i];if(!float.IsFinite(v))continue;sum+=(double)v*v;peak=Math.Max(peak,Math.Abs(v));}n+=read;}return new Sound{Path=relative,Name=Path.GetFileNameWithoutExtension(input),NormalizationGain=MixProcessor.NormalizationGain(n==0?0:sum/n,peak),Analyzed=true};}catch{File.Delete(target);throw;}});
     sound.PropertyChanged+=SoundChanged;Sounds.Add(sound);SelectedSound=sound;imported++;
    }
    catch(Exception ex){errors.Add(Path.GetFileName(input)+": "+ex.Message);}
   }
   RebuildCollections();Save();Changed(nameof(LibraryCount));Notice=$"Imported {imported} sound(s). "+(errors.Count>0?string.Join("; ",errors):"Files copied into WarMusic's portable library.");
  }
  finally{Busy=false;}
 }
 void PlaySelected(bool preview){if(SelectedSound==null)throw new InvalidOperationException("Select a sound first.");if(!Engine.Running&&preview)Engine.Start(Profile,false);Engine.Play(SelectedSound,preview,Profile.Normalize);}
 void PlayNext(){if(Queue.Count==0)return;var next=Queue[0];Queue.RemoveAt(0);Safe(()=>Engine.Play(next,false,Profile.Normalize));}
 void CreateProfile()
 {
  if(string.IsNullOrWhiteSpace(ProfileName))throw new InvalidOperationException("Enter a profile name.");if(Profiles.Any(p=>p.Name.Equals(ProfileName.Trim(),StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("That profile name already exists.");
  var copy=System.Text.Json.JsonSerializer.Deserialize<Profile>(System.Text.Json.JsonSerializer.Serialize(Profile))!;copy.Name=ProfileName.Trim();copy.PropertyChanged+=ProfileChanged;Profiles.Add(copy);Profile=copy;ProfileName="";Save();Notice="Profile saved. Music transmission is off.";
 }
 public void AttachHotkeys(IntPtr window){hotkeys=new(window);RegisterHotkeys();}
 void RegisterHotkeys()
 {
  if(hotkeys==null)return;hotkeys.Clear();var errors=new List<string>();
  void Add(string key,Action action){try{hotkeys.Register(key,()=>Safe(action));}catch(Exception ex){errors.Add(ex.Message);}}
  Add("Control+Shift+F7",()=>FadeMusic(true));Add("Control+Shift+F8",()=>FadeMusic(false));
  Add(Profile.PanicKey,()=>Engine.Panic());Add(Profile.ToggleKey,()=>{if(!Measuring&&Profile.TransmitMode==0)Engine.Toggle();});Add(Profile.PlayKey,()=>Engine.Pause());
  foreach(var s in Sounds)if(!string.IsNullOrWhiteSpace(s.Hotkey)){var sound=s;Add(s.Hotkey,()=>Engine.Play(sound,false,Profile.Normalize));}
  foreach(var key in new[]{Profile.CommsKey,Profile.HoldKey})try{var parsed=new KeyConverter().ConvertFromInvariantString(key);if(parsed is not Key k||k==Key.None)throw new FormatException();}catch{errors.Add($"'{key}' is not a valid single key. Use a name such as CapsLock or F8.");}
  HotkeyState=errors.Count==0?"Hotkeys registered. Hold/comms keys are observed without intercepting them.":string.Join("\n",errors);
 }
 void Tick()
 {
  if(disposed)return;
  musicPower=Engine.Routing?.85*musicPower+.15*Engine.MusicRms*Engine.MusicRms:0;musicPeak=Engine.Routing?Math.Max(Engine.MusicPeak,musicPeak*.92f):0;
  foreach(var name in new[]{nameof(MusicBoost),nameof(BoostLabel),nameof(GameMusicVolume),nameof(GameMusicLabel),nameof(MicLabel),nameof(ClipLabel),nameof(MonitorLabel),nameof(SentMusicMeter),nameof(MusicLevelStatus),nameof(MusicLevelColor)})Changed(name);
  var p=Profile;Engine.Configure(new(){MicGain=p.MicGain,AppGain=p.AppGain,ClipGain=p.ClipGain,SendGain=p.SendGain,MonitorGain=p.MonitorGain,DuckEnabled=p.DuckEnabled,DuckMode=p.DuckMode,ThresholdDb=p.ThresholdDb,ReductionDb=p.ReductionDb,AttackMs=p.AttackMs,HoldMs=p.HoldMs,ReleaseMs=p.ReleaseMs,DuckMonitor=p.DuckMonitor,ClipDucking=p.ClipDucking,MonitorApplication=p.MonitorApplication,MicrophoneMonitorGain=LiveMicLevel});
  Engine.SetComms(Hotkeys.IsDown(p.CommsKey));if(Measuring)Engine.Panic();else if(p.TransmitMode==1)Engine.Hold(Hotkeys.IsDown(p.HoldKey));
  foreach(var name in new[]{nameof(Transmission),nameof(Listening),nameof(Track),nameof(PlayLabel),nameof(TimeLabel),nameof(Duration),nameof(MicMeter),nameof(MusicMeter),nameof(OutputMeter),nameof(DuckStatus),nameof(Limiter),nameof(TestStatus),nameof(RouteStatus)})Changed(name);
  if(!seeking)Changed(nameof(Position));
  foreach(var name in new[]{nameof(TrackDetails),nameof(LocalTransport),nameof(CanTogglePlayback),nameof(CanSeek),nameof(LiveMic),nameof(LiveMicStatus)})Changed(name);
  Changed(nameof(MusicButtonLabel));Changed(nameof(IsRouting));Changed(nameof(SessionLabel));Changed(nameof(SessionColor));

  if(tick%150==0)_=Recover();
  if(++tick%30==0&&Engine.SourcePid!=0){try{using var process=Process.GetProcessById(Engine.SourcePid);if(process.HasExited)throw new InvalidOperationException();SourceState=Engine.AppPeak>.0001?(Engine.Transmitting?"Application music is being sent to the cable":"Receiving music · press Enable music to send it"):"Connected · source silent";}catch{Engine.DisconnectSource();SourceState="Source closed · Refresh and reconnect";}}
 }
 public void BeginSeek()=>seeking=true;
 public void Seek(double value){try{Engine.Seek(value);}catch(Exception ex){Notice=ex.Message;}finally{seeking=false;}}
 public void Save(){foreach(var p in Profiles)Store.Sanitize(p);settings.Profiles=Profiles.ToList();settings.Sounds=Sounds.ToList();settings.ActiveProfile=Profile.Name;Store.Save(settings);}
 public void Dispose(){disposed=true;StopRecovery();timer.Stop();hotkeys?.Dispose();Engine.Dispose();Save();}
}











