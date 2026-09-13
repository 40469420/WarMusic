using WarMusic.Audio;
using WarMusic.Models;
using WarMusic.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;
using System.Runtime.InteropServices;

if(args.Contains("--live-route"))
{
 using var e=new MMDeviceEnumerator();
 foreach(var d in e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))using(d){var sessions=d.AudioSessionManager.Sessions;for(int i=0;i<sessions.Count;i++)using(var session=sessions[i]){try{using var p=Process.GetProcessById((int)session.GetProcessID);if(p.ProcessName is "WarMusic" or "Spotify")Console.WriteLine($"{p.ProcessName} pid={p.Id} endpoint={d.FriendlyName} endpointLevel={d.AudioEndpointVolume.MasterVolumeLevelScalar} endpointDb={d.AudioEndpointVolume.MasterVolumeLevel} sessionVolume={session.SimpleAudioVolume.Volume} muted={session.SimpleAudioVolume.Mute} peak={session.AudioMeterInformation.MasterPeakValue}");}catch{}}}
 var source=Devices.ResolveApplication(new(Process.GetProcessesByName("Spotify")[0].Id,"Spotify"));using var spotify=new ProcessCapture();using var app=new ProcessCapture();using var device=e.GetDevice(Devices.List(DataFlow.Capture).First(d=>d.Name.StartsWith("CABLE Output")).Id);using var cable=new WasapiCapture(device);
 var sb=new BufferedWaveProvider(ProcessCapture.Format){BufferDuration=TimeSpan.FromSeconds(10)};var ab=new BufferedWaveProvider(ProcessCapture.Format){BufferDuration=TimeSpan.FromSeconds(10)};var cb=new BufferedWaveProvider(cable.WaveFormat){BufferDuration=TimeSpan.FromSeconds(10)};
 spotify.Data+=(b,n)=>sb.AddSamples(b,0,n);app.Data+=(b,n)=>ab.AddSamples(b,0,n);cable.DataAvailable+=(_,a)=>cb.AddSamples(a.Buffer,0,a.BytesRecorded);
 await spotify.StartAsync(source.Pid);await app.StartAsync(Process.GetProcessesByName("WarMusic")[0].Id);cable.StartRecording();await Task.Delay(4000);spotify.Dispose();app.Dispose();cable.StopRecording();
 float[] Read(BufferedWaveProvider b){var f=new float[48000*2*4];Devices.Stereo48(b.ToSampleProvider()).Read(f,0,f.Length);double l=0,r=0,m=0;for(int i=0;i<f.Length;i+=2){l+=f[i]*f[i];r+=f[i+1]*f[i+1];m+=Math.Pow((f[i]+f[i+1])/2,2);}Console.WriteLine($"Channels L={Math.Sqrt(l/(f.Length/2)):F5} R={Math.Sqrt(r/(f.Length/2)):F5} MONO={Math.Sqrt(m/(f.Length/2)):F5}");return Enumerable.Range(0,f.Length/24).Select(i=>f[i*24]).ToArray();}
 var sp=Read(sb);var ap=Read(ab);var ca=Read(cb);
 double Rms(float[] a)=>Math.Sqrt(a.Average(x=>(double)x*x));
 double Correlation(float[] a,float[] b){double best=0;for(int lag=-1600;lag<=1600;lag++){double xy=0,xx=0,yy=0;for(int i=2000;i<Math.Min(a.Length,b.Length)-2000;i++){double x=a[i],y=b[i+lag];xy+=x*y;xx+=x*x;yy+=y*y;}best=Math.Max(best,Math.Abs(xy)/Math.Sqrt(Math.Max(1e-30,xx*yy)));}return best;}
 Console.WriteLine($"LIVE RMS Spotify={Rms(sp):F6}, WarMusic={Rms(ap):F6}, cable={Rms(ca):F6}");Console.WriteLine($"Music correlation: WarMusic={Correlation(sp,ap):F4}; cable={Correlation(sp,ca):F4}; WarMusic vs cable={Correlation(ap,ca):F4}");return 0;
}
if(args.Contains("--spotify-route"))
{
 var project=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));Store.Initialize(project);
 var settings=Store.Load();var profile=settings.Profiles.First(p=>p.Name==settings.ActiveProfile);
 using var e=new MMDeviceEnumerator();using var device=e.GetDevice(Devices.List(DataFlow.Capture).First(d=>d.Name.StartsWith("CABLE Output")).Id);
 using var receiving=new WasapiCapture(device);var buffer=new BufferedWaveProvider(receiving.WaveFormat){BufferDuration=TimeSpan.FromSeconds(5),DiscardOnBufferOverflow=true};receiving.DataAvailable+=(_,a)=>buffer.AddSamples(a.Buffer,0,a.BytesRecorded);receiving.StartRecording();
 using var engine=new AudioEngine(new SignalGenerator(48000,2){Gain=0});
 engine.Configure(new(){DuckEnabled=profile.DuckEnabled,AppGain=profile.AppGain,SendGain=profile.SendGain,MonitorGain=0});engine.Start(profile,true);
 var selected=Devices.ResolveApplication(new(Process.GetProcessesByName("Spotify").Min(p=>p.Id),"Spotify"));
 var listed=Devices.Applications().Where(a=>a.Name.Equals("Spotify",StringComparison.OrdinalIgnoreCase)).ToList();if(listed.Count!=1||listed[0].Pid!=selected.Pid)throw new Exception("Spotify source list did not resolve its audio session");
 await engine.ConnectSource(selected.Pid);engine.Toggle();await Task.Delay(500);buffer.ClearBuffer();await Task.Delay(2000);
 var samples=new float[48000*2];Devices.Stereo48(buffer.ToSampleProvider()).Read(samples,0,samples.Length);
 double rms=Math.Sqrt(samples.Average(v=>(double)v*v));Console.WriteLine($"Actual Spotify PID={selected.Pid}, captured peak={engine.AppPeak:F5}; CABLE Output RMS={rms:F5}; music enabled={engine.Transmitting}");
 engine.Panic();await Task.Delay(300);buffer.ClearBuffer();await Task.Delay(1200);Devices.Stereo48(buffer.ToSampleProvider()).Read(samples,0,samples.Length);double muted=Math.Sqrt(samples.Average(v=>(double)v*v));
 Console.WriteLine($"CABLE Output after music mute RMS={muted:F5}");receiving.StopRecording();
 bool pass=rms>.0005&&muted<rms*.1;Console.WriteLine(pass?"PASS Actual Spotify reaches CABLE Output and mute removes it":"FAIL Spotify route verification");return pass?0:1;
}
if(args.Length>=3&&args[0]=="--tone")
{
 using var enumerator=new MMDeviceEnumerator();using var device=enumerator.GetDevice(args[2]);
 using var player=new WasapiOut(device,AudioClientShareMode.Shared,true,50);
 player.Init(new SignalGenerator(48000,2){Frequency=double.Parse(args[1]),Gain=.02,Type=SignalGeneratorType.Sin});
 await Task.Delay(1200);player.Play();await Task.Delay(args.Length>3?int.Parse(args[3]):1800);player.Stop();return 0;
}

var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
Store.Initialize(Path.Combine(root,"tests","scratch"));
int failed=0,passed=0;
void Check(string name,Action action){try{action();Console.WriteLine("PASS "+name);passed++;}catch(Exception ex){Console.WriteLine("FAIL "+name+": "+ex.Message);failed++;}}
void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
float[] Filled(float n)=>Enumerable.Repeat(n,960).ToArray();
var o=new MixOptions{DuckEnabled=false,MicGain=1,AppGain=1,ClipGain=1,SendGain=1,MonitorGain=1};
Check("Boost lifts quiet music without changing voice",()=>{var m=new MixProcessor();var send=new float[960];var monitor=new float[960];m.Process(Filled(.1f),Filled(.005f),Filled(0),send,monitor,960,o with {AppGain=16},true,false,false,false);Assert(Math.Abs(send[0]-.18f)<.0001,"Quiet music boost incorrect");Assert(Math.Abs(m.MusicRms-.08f)<.0001,"Music meter includes microphone or ignores boost");m.Process(Filled(.1f),Filled(.005f),Filled(0),send,monitor,960,o with {AppGain=16},false,false,false,false);Assert(m.MusicRms==0&&m.MusicPeak==0,"Muted music meter still active");Assert(Math.Abs(send[0]-.1)<.0001,"Muted boost changed microphone");});
Check("Boost is limited and survives profile save",()=>{var m=new MixProcessor();var send=new float[960];m.Process(Filled(.2f),Filled(.2f),Filled(0),send,new float[960],960,o with {AppGain=16},true,false,false,false);Assert(m.Limited&&send.All(v=>v<=.96f),"Boost escaped limiter");var settings=new Settings();settings.Profiles[0].AppGain=16;Store.Sanitize(settings.Profiles[0]);Store.Save(settings);Assert(Store.Load().Profiles[0].AppGain==16,"Boost truncated on save");});Check("Fades reverse smoothly and panic resets state",()=>{var f=new MusicFade();f.Begin(true,2,false);f.Step(.5f);Assert(Math.Abs(f.Gain-.25f)<.0001,"Fade in ramp incorrect");f.Begin(false,1,true);Assert(Math.Abs(f.Gain-.25f)<.0001,"Reversing fade jumped");Assert(f.Step(1)&&f.Gain==0&&!f.Active,"Fade out did not finish");f.Reset();Assert(f.Gain==1&&!f.Active,"Panic did not reset fade");});
Check("Level advice boosts quiet sources and protects peaks",()=>{Assert(Math.Abs(LevelAdvisor.Recommend(.01,.04f,1)-10)<.001,"Quiet recommendation wrong");Assert(LevelAdvisor.Recommend(.01,.8f,1)<1,"Peak ceiling ignored");bool rejected=false;try{LevelAdvisor.Recommend(0,0,1);}catch(InvalidOperationException){rejected=true;}Assert(rejected,"Silence accepted");});
Check("Portable backup round trip preserves sounds and hotkeys",()=>{string file=Path.Combine(Store.Data,"backup-source.wav"),archive=Path.Combine(Store.Data,"roundtrip.warmusic");using(var wav=new WaveFileWriter(file,AudioEngine.Format))wav.WriteSamples(new float[960],0,960);var data=new Settings();data.Profiles[0].ToggleKey="Control+F6";data.Sounds.Add(new(){Path=file,Name="Round trip",Hotkey="Control+1",Color="#365E70"});PortableBackup.Export(archive,data);var imported=PortableBackup.Import(archive);Assert(imported.Sounds.Count==1&&imported.Sounds[0].Hotkey=="Control+1","Sound metadata lost");Assert(imported.Profiles[0].ToggleKey=="Control+F6","Profile hotkey lost");Assert(File.Exists(Path.Combine(Store.Root,imported.Sounds[0].Path)),"Audio not restored");Assert(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(Store.Root,imported.Sounds[0].Path))),"Audio changed in backup");});
Check("Malformed backup cannot escape library or replace settings",()=>{string archive=Path.Combine(Store.Data,"hostile.warmusic");if(File.Exists(archive))File.Delete(archive);using(var zip=System.IO.Compression.ZipFile.Open(archive,System.IO.Compression.ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("settings.json").Open());writer.Write(System.Text.Json.JsonSerializer.Serialize(new Settings{Sounds=[new(){Path="sounds/../../settings.json"}]}));}int before=Directory.GetDirectories(Path.Combine(Store.Data,"library")).Length;bool rejected=false;try{PortableBackup.Import(archive);}catch(InvalidDataException){rejected=true;}Assert(rejected,"Unsafe path accepted");Assert(Directory.GetDirectories(Path.Combine(Store.Data,"library")).Length==before,"Rejected backup changed library");});Check("Music mute preserves microphone",()=>{var m=new MixProcessor();var send=new float[960];var monitor=new float[960];m.Process(Filled(.1f),Filled(.3f),Filled(.2f),send,monitor,960,o,false,false,false,true);Assert(send.All(x=>Math.Abs(x-.1f)<.00001),"Music leaked into muted output");});
Check("Private preview cannot enter game mix",()=>{var m=new MixProcessor();var send=new float[960];var monitor=new float[960];m.Process(Filled(.1f),Filled(0),Filled(.4f),send,monitor,960,o,true,true,false,true);Assert(send.All(x=>Math.Abs(x-.1f)<.00001),"Preview leaked");Assert(monitor.All(x=>Math.Abs(x-.4f)<.00001),"Preview absent from headphones");});
Check("Application monitor defaults off",()=>{var m=new MixProcessor();var send=new float[960];var monitor=new float[960];m.Process(Filled(0),Filled(.3f),Filled(0),send,monitor,960,o,true,false,false,false);Assert(monitor.All(x=>x==0),"Duplicate app playback");Assert(send[0]>.29,"App missing from transmission");});
Check("Peak limiter bounds summed sources",()=>{var m=new MixProcessor();var send=new float[960];var monitor=new float[960];m.Process(Filled(2),Filled(2),Filled(2),send,monitor,960,o,true,false,false,true);Assert(send.All(x=>x<=.96f),"Limiter exceeded ceiling");Assert(m.Limited,"Limiter indicator missing");Assert(MixProcessor.Limit(float.NaN)==0,"Invalid input not silenced");});
Check("Panic blocks held key until released",()=>{var g=new TransmissionGate();g.Hold(true);Assert(g.Enabled,"Hold did not start");g.Panic();g.Hold(true);Assert(!g.Enabled,"Panic immediately rearmed");g.Hold(false);g.Hold(true);Assert(g.Enabled,"Fresh press did not rearm");});
Check("Panic clears toggle latch",()=>{var g=new TransmissionGate();g.Toggle();g.Panic();Assert(!g.Enabled,"Toggle survived panic");g.Toggle();Assert(g.Enabled,"Deliberate reactivation failed");});
Check("Voice ducking attack hold release",()=>{var d=new DuckEnvelope();var settings=new MixOptions();for(int i=0;i<20;i++)d.Step(.2f,false,settings,10);Assert(d.Gain<.2,"Attack did not lower music");for(int i=0;i<20;i++)d.Step(0,false,settings,10);Assert(d.Gain<.2,"Hold did not bridge pause");for(int i=0;i<400;i++)d.Step(0,false,settings,10);Assert(d.Gain>.99,"Release did not restore music");});
Check("Key-only mode ignores voice",()=>{var d=new DuckEnvelope();var settings=new MixOptions{DuckMode=1};for(int i=0;i<30;i++)d.Step(.5f,false,settings,10);Assert(d.Gain>.999,"Voice triggered key-only ducking");for(int i=0;i<30;i++)d.Step(0,true,settings,10);Assert(d.Gain<.2,"Comms key did not duck");});
Check("Threshold hysteresis avoids chatter",()=>{var d=new DuckEnvelope();var settings=new MixOptions{HoldMs=0,AttackMs=1};float t=MathF.Pow(10,settings.ThresholdDb/20);d.Step(t*1.1f,false,settings,10);for(int i=0;i<20;i++)d.Step(t*.85f,false,settings,10);Assert(d.Gain<.2,"Voice gate chattered inside hysteresis band");});
Check("Ducking keeps stereo gains equal",()=>{var m=new MixProcessor();var send=new float[960];m.Process(Filled(.1f),Filled(.3f),Filled(0),send,new float[960],960,new MixOptions(),true,false,false,false);for(int i=0;i<960;i+=2)Assert(send[i]==send[i+1],"Stereo image shifted");});
Check("Normalization does not amplify silence or exceed +6dB",()=>{Assert(MixProcessor.NormalizationGain(0,0)==1,"Silence boosted");Assert(MixProcessor.NormalizationGain(.0001,.02f)<=2,"Excessive gain");Assert(MixProcessor.NormalizationGain(.25,1)<=.95,"Loud track not reduced");});
Check("Settings round trip and bounds",()=>{var s=new Settings();s.Profiles[0].MicGain=100;Store.Sanitize(s.Profiles[0]);Store.Save(s);var loaded=Store.Load();Assert(loaded.Profiles[0].MicGain==2,"Gain not bounded");Assert(loaded.Profiles.Count==3,"Profiles lost");});
Check("Corrupt settings recover and preserve original",()=>{File.WriteAllText(Path.Combine(Store.Data,"settings.json"),"broken JSON");var s=Store.Load();Assert(s.Profiles.Count==3,"Defaults missing");Assert(Directory.GetFiles(Store.Data,"settings.json.corrupt-*").Length>0,"Corrupt file not preserved");});
Check("Mono resampling produces stereo 48k",()=>{var source=new SignalGenerator(22050,1){Frequency=440,Gain=.1,Type=SignalGeneratorType.Sin};var converted=Devices.Stereo48(source);var values=new float[960];Assert(converted.Read(values,0,960)==960,"Incorrect read");Assert(converted.WaveFormat.SampleRate==48000&&converted.WaveFormat.Channels==2,"Incorrect format");for(int i=0;i<960;i+=2)Assert(Math.Abs(values[i]-values[i+1])<.00001,"Mono conversion mismatch");});
Console.WriteLine("\nACTIVE PLAYBACK DEVICES");foreach(var d in Devices.List(DataFlow.Render))Console.WriteLine(d.Name+" | "+d.Id);
Check("Live mic monitor is independent and opt-in",()=>{var m=new MixProcessor();var send=new float[960];var monitor=new float[960];m.Process(Filled(.2f),Filled(0),Filled(0),send,monitor,960,o,false,false,false,false);Assert(monitor.All(x=>x==0),"Mic monitor started by default");m.Process(Filled(.2f),Filled(0),Filled(0),send,monitor,960,o with {MonitorMicrophone=true,MicrophoneMonitorGain=.25f,MonitorGain=0},false,false,false,false);Assert(monitor.All(x=>Math.Abs(x-.05f)<.00001),"Listening gain failed");Assert(send.All(x=>Math.Abs(x-.2f)<.00001),"Listening changed outgoing voice");});
Check("Windows media session matching",()=>{Assert(WindowsMedia.Matches("Spotify.exe","Spotify"),"Desktop mismatch");Assert(WindowsMedia.Matches("SpotifyAB.SpotifyMusic_xyz!Spotify","Spotify"),"Packaged mismatch");Assert(!WindowsMedia.Matches("Other.exe","Spotify"),"Wrong player matched");Assert(!WindowsMedia.Matches("Other.exe",""),"Empty preference matched");});
if(args.Contains("--media-test")){var media=new WindowsMedia();await media.RefreshAsync("Spotify");Check("Windows Now Playing API",()=>Assert(media.LastError==null,media.LastError??""));Console.WriteLine($"Media source: {media.Current.Source}; title available: {media.Current.Title!="Nothing playing"}; artist available: {!string.IsNullOrWhiteSpace(media.Current.Artist)}");}
Console.WriteLine("\nACTIVE RECORDING DEVICES");foreach(var d in Devices.List(DataFlow.Capture))Console.WriteLine(d.Name+" | "+d.Id);
if(args.Contains("--capture-test"))
{
 try
 {
  using var capture=new ProcessCapture();long bytes=0;capture.Data+=(_,n)=>Interlocked.Add(ref bytes,n);await capture.StartAsync(Environment.ProcessId);await Task.Delay(600);Console.WriteLine("PASS Windows process-loopback activation and clean shutdown (no microphone capture)");passed++;
 }
 catch(Exception ex){Console.WriteLine("FAIL Windows process-loopback activation: "+ex);failed++;}
}
if(args.Contains("--isolation-test"))
{
 try
 {
  var cable=Devices.List(DataFlow.Render).First(d=>d.Name.StartsWith("CABLE Input",StringComparison.OrdinalIgnoreCase));
  Process Tone(int hz){var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--tone");start.ArgumentList.Add(hz.ToString());start.ArgumentList.Add(cable.Id);return Process.Start(start)!;}
  using var selected=Tone(440);using var unrelated=Tone(880);using var capture=new ProcessCapture();
  var pcm=new List<short>();var sync=new object();capture.Data+=(b,n)=>{lock(sync){for(int i=0;i<n;i+=4)pcm.Add(BitConverter.ToInt16(b,i));}};
  await capture.StartAsync(selected.Id);await Task.WhenAll(selected.WaitForExitAsync(),unrelated.WaitForExitAsync());await Task.Delay(150);capture.Dispose();
  double Power(int hz){double re=0,im=0;for(int i=0;i<pcm.Count;i++){double phase=2*Math.PI*hz*i/48000;re+=pcm[i]*Math.Cos(phase);im+=pcm[i]*Math.Sin(phase);}return Math.Sqrt(re*re+im*im)/Math.Max(1,pcm.Count);}
  double wanted=Power(440),other=Power(880);Assert(wanted>10,$"Selected signal missing ({wanted:0.00})");Assert(other<wanted*.1,$"Other application leaked ({other:0.00} vs {wanted:0.00})");
  Console.WriteLine($"PASS Selected-process signal capture and sibling-process isolation ({wanted:0.00} selected / {other:0.00} unrelated)");passed++;
 }
 catch(Exception ex){Console.WriteLine("FAIL Process isolation: "+ex);failed++;}
}
Console.WriteLine($"\n{passed} passed; {failed} failed.");
if(args.Contains("--engine-test"))
{
 try
 {
  var cable=Devices.List(DataFlow.Render).First(d=>d.Name.StartsWith("CABLE Input",StringComparison.OrdinalIgnoreCase));
  var silentMonitor=Devices.List(DataFlow.Render).First(d=>d.Name.Contains("S/PDIF"));
  var path=Path.Combine(Store.Data,"synthetic-test.wav");
  using(var wav=new WaveFileWriter(path,AudioEngine.Format))
  {var provider=new SignalGenerator(48000,2){Frequency=660,Gain=.05,Type=SignalGeneratorType.Sin};var values=new float[960];for(int i=0;i<300;i++){provider.Read(values,0,values.Length);wav.WriteSamples(values,0,values.Length);}}
  using var engine=new AudioEngine(new SignalGenerator(48000,2){Frequency=220,Gain=.05,Type=SignalGeneratorType.Sin});
  string? error=null;engine.Error+=text=>error=text;
  engine.Configure(new(){DuckEnabled=false,MicGain=1,ClipGain=1,SendGain=1,MonitorGain=0});engine.Start(new(){MonitorId=silentMonitor.Id,CableId=cable.Id},true);
  var receiving=Devices.List(DataFlow.Capture).First(d=>d.Name.StartsWith("CABLE Output",StringComparison.OrdinalIgnoreCase));
  using var e=new MMDeviceEnumerator();using var device=e.GetDevice(receiving.Id);using var loopback=new WasapiCapture(device);
  Console.WriteLine($"Cable recording endpoint: {device.FriendlyName}; muted={device.AudioEndpointVolume.Mute}; level={device.AudioEndpointVolume.MasterVolumeLevelScalar:0.00}");
  var buffer=new BufferedWaveProvider(loopback.WaveFormat){BufferDuration=TimeSpan.FromSeconds(3),DiscardOnBufferOverflow=true};loopback.DataAvailable+=(_,a)=>buffer.AddSamples(a.Buffer,0,a.BytesRecorded);loopback.StartRecording();
  async Task<(double voice,double music)> Measure()
  {
   await Task.Delay(200);buffer.ClearBuffer();await Task.Delay(600);var samples=Devices.Stereo48(buffer.ToSampleProvider());float[] values=new float[48000];samples.Read(values,0,values.Length);
   double Power(int hz){double re=0,im=0;for(int i=0;i<values.Length/2;i++){double phase=2*Math.PI*hz*i/48000;re+=values[i*2]*Math.Cos(phase);im+=values[i*2]*Math.Sin(phase);}return Math.Sqrt(re*re+im*im)/(values.Length/2);}
   return (Power(220),Power(660));
  }
  engine.SetLoop(true);engine.Toggle();engine.Play(new(){Path=path,Name="test"},false,false);var mixed=await Measure();Assert(mixed.voice>.005&&mixed.music>.005,$"Sources missing at cable: {mixed}");
  engine.FadeTo(false,.1f);await Task.Delay(200);var faded=await Measure();Assert(faded.voice>.005&&faded.music<.001&&!engine.Transmitting,"Fade-out failed at cable");engine.FadeTo(true,.1f);await Task.Delay(200);var fadedIn=await Measure();Assert(fadedIn.music>.005,"Fade-in failed at cable");
  engine.Panic();var panic=await Measure();Assert(panic.voice>.005&&panic.music<.001,$"Panic failed at cable: {panic}");
  engine.Toggle();engine.Play(new(){Path=path,Name="preview"},true,false);var preview=await Measure();Assert(preview.voice>.005&&preview.music<.001,$"Preview leaked at cable: {preview}");
  engine.StopLocal();loopback.StopRecording();engine.Stop();Assert(error==null,error??"");
  Console.WriteLine("PASS Actual cable output: synthetic microphone + clip, panic preserves voice, private preview isolation");passed++;
 }
 catch(Exception ex){Console.WriteLine("FAIL End-to-end engine: "+ex);failed++;}
 Console.WriteLine($"FINAL: {passed} passed; {failed} failed.");
}
if(args.Contains("--routing-regression"))
{
 try
 {
  var cable=Devices.List(DataFlow.Render).First(d=>d.Name.StartsWith("CABLE Input"));var monitor=Devices.List(DataFlow.Render).First(d=>d.Name.Contains("S/PDIF"));
  var profile=new Profile{CableId=cable.Id,MonitorId=monitor.Id};
  using var engine=new AudioEngine(new SignalGenerator(48000,2){Frequency=220,Gain=.05,Type=SignalGeneratorType.Sin});
  engine.Configure(new(){DuckEnabled=false,AppGain=1,SendGain=1,MonitorGain=0,MicrophoneMonitorGain=0});
  engine.SetLiveMicrophone(profile,true);Assert(engine.LiveMicrophone&&!engine.Routing,"Standalone mic test should not create a game route");engine.SetLiveMicrophone(profile,false);Assert(!engine.Running,"Standalone mic test did not release its devices");
  engine.Start(profile,true);engine.SetLiveMicrophone(profile,true);Assert(engine.Routing&&engine.LiveMicrophone,"Live monitor interrupted active route");engine.SetLiveMicrophone(profile,false);Assert(engine.Routing,"Stopping live monitor stopped the game route");
  Console.WriteLine("PASS Live mic test starts privately and preserves an existing route");passed++;
  var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{"--tone","440",monitor.Id,"6000"})start.ArgumentList.Add(arg);
  using var player=Process.Start(start)!;await engine.ConnectSource(player.Id);engine.Toggle();await Task.Delay(1600);
  using var endpointEnumerator=new MMDeviceEnumerator();using var receivingEndpoint=endpointEnumerator.GetDevice(Devices.List(DataFlow.Capture).First(d=>d.Name.StartsWith("CABLE Output")).Id);using var gameInput=new WasapiCapture(receivingEndpoint);
  var gameBuffer=new BufferedWaveProvider(gameInput.WaveFormat){BufferDuration=TimeSpan.FromSeconds(2),DiscardOnBufferOverflow=true};gameInput.DataAvailable+=(_,a)=>gameBuffer.AddSamples(a.Buffer,0,a.BytesRecorded);gameInput.StartRecording();
  async Task<double> GameMusicPower(){gameBuffer.ClearBuffer();await Task.Delay(300);var converted=Devices.Stereo48(gameBuffer.ToSampleProvider());var values=new float[24000];converted.Read(values,0,values.Length);double re=0,im=0;for(int i=0;i<values.Length/2;i++){double phase=2*Math.PI*440*i/48000;re+=values[i*2]*Math.Cos(phase);im+=values[i*2]*Math.Sin(phase);}return Math.Sqrt(re*re+im*im)/(values.Length/2);}
  Assert(await GameMusicPower()>.002,"Application music did not reach game input");
  engine.BeginLevelProbe();engine.BeginTest();await Task.Delay(400);Assert(engine.SourcePid==player.Id,"Recording disconnected the application");Assert(engine.Transmitting,"Recording disabled music");Assert(engine.AppPeak>.001,"Application capture stopped during test");engine.EndTest();var probe=engine.EndLevelProbe();Assert(probe.Rms>.005&&probe.Peak>.01,"Level measurement lost application audio");
  Assert(await GameMusicPower()>.002,"Application music stopped at game input after test");
  engine.PreviewTest();engine.PreviewTest();engine.StopLocal();
  using(var reader=new AudioFileReader(Path.Combine(Store.Data,"mix-test.wav")))
  {
   var samples=new float[(int)reader.Length/4];int count=reader.Read(samples,0,samples.Length);
   double Power(int hz){double re=0,im=0;for(int i=0;i<count/2;i++){double phase=2*Math.PI*hz*i/48000;re+=samples[i*2]*Math.Cos(phase);im+=samples[i*2]*Math.Sin(phase);}return Math.Sqrt(re*re+im*im)/Math.Max(1,count/2);}
   Assert(Power(220)>.005,"Test lost synthetic microphone");Assert(Power(440)<.001,"Application audio leaked into saved test");
  }
  Assert(engine.SourcePid==player.Id,"Listening to test disconnected music");
  Assert(await GameMusicPower()>.002,"Test playback stopped application music at game input");gameInput.StopRecording();
  Console.WriteLine("PASS Mix test keeps application connected, excludes it from recording, and supports repeated private playback");passed++;
  engine.Stop();await player.WaitForExitAsync();
 }
 catch(Exception ex){Console.WriteLine("FAIL Routing regression: "+ex);failed++;}
}
if(args.Contains("--recovery-test"))
{
 var completed=new TaskCompletionSource<Exception?>();
 var thread=new Thread(()=>{
  var dispatcher=System.Windows.Threading.Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
  var application=new System.Windows.Application{ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown};
  dispatcher.BeginInvoke(async ()=>{
   try{
    var monitor=Devices.List(DataFlow.Render).First(d=>d.Name.Contains("S/PDIF"));
    var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{"--tone","440",monitor.Id,"15000"})start.ArgumentList.Add(arg);
    using var source=Process.Start(start)!;await Task.Delay(1600);
    using var vm=new MainViewModel(new AudioEngine(new SignalGenerator(48000,2){Gain=0}));vm.Profile.CableId=Devices.List(DataFlow.Render).First(d=>d.Name.StartsWith("CABLE Input")).Id;vm.Profile.MonitorId=monitor.Id;vm.Profile.MicId=Devices.List(DataFlow.Capture).First(d=>d.Name.StartsWith("Microphone")).Id;vm.Profile.SourceName=source.ProcessName;
    await vm.Ready();Assert(vm.Engine.Routing&&vm.Engine.SourcePid==source.Id&&!vm.Engine.Transmitting,"Ready did not connect muted");
    vm.Engine.Toggle();vm.Engine.Stop();await vm.Recover();Assert(vm.Engine.Routing&&vm.Engine.SourcePid==source.Id&&!vm.Engine.Transmitting,"Device recovery did not reconnect muted");
    vm.MeasureCommand.Execute(null);while(vm.Measuring)await Task.Delay(100);Assert(vm.CanApplyLevel,"Measurement did not produce advice: "+vm.LevelStatus);vm.ApplyLevelCommand.Execute(null);Assert(vm.Profile.AppGain>1&&!vm.Engine.Transmitting,"Applying level enabled music or lost gain");
    if(!source.HasExited){source.Kill();await source.WaitForExitAsync();}using var replacement=Process.Start(start)!;await Task.Delay(1600);await vm.Recover();Assert(vm.Engine.SourcePid==replacement.Id&&!vm.Engine.Transmitting,"Restarted source was not recovered muted");
    vm.DisconnectCommand.Execute(null);await vm.Recover();Assert(vm.Engine.SourcePid==0,"Manual disconnect was undone by recovery");
    vm.StopRouteCommand.Execute(null);await vm.Recover();Assert(!vm.Engine.Running,"Manual stop was undone by recovery");if(!replacement.HasExited){replacement.Kill();await replacement.WaitForExitAsync();}completed.SetResult(null);
   }catch(Exception ex){completed.SetResult(ex);}finally{application.Shutdown();}
  });application.Run();
 });thread.SetApartmentState(ApartmentState.STA);thread.Start();var error=await completed.Task;thread.Join();Check("Ready, automatic recovery, manual stop and disconnect",()=>{if(error!=null)throw error;});
}Console.WriteLine($"TOTAL: {passed} passed; {failed} failed.");
return failed==0?0:1;













