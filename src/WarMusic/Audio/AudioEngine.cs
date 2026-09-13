using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using WarMusic.Models;
namespace WarMusic.Audio;
public sealed class AudioEngine:IDisposable
{
 readonly ISampleProvider? suppliedMicrophone;
 public AudioEngine(ISampleProvider? microphoneSource=null){suppliedMicrophone=microphoneSource;}
 public static readonly WaveFormat Format=WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
 readonly object audioLock=new();readonly object sourceLock=new();readonly object gateLock=new();
 readonly MusicFade fade=new();
 public bool Fading {get{lock(gateLock)return fade.Active;}}
 public void FadeTo(bool on,float seconds){lock(gateLock){if(!Routing)return;fade.Begin(on,seconds,gate.Enabled);if(on)gate.Set(true);}}
 readonly TransmissionGate gate=new();readonly MixProcessor mixer=new();
 MMDeviceEnumerator? enumerator;readonly List<MMDevice> devices=[];
 WasapiCapture? microphone;WasapiOut? output,headphones;
 BufferedWaveProvider? micBuffer,appBuffer,outBuffer,monitorBuffer;
 ISampleProvider? micSamples,appSamples,localSamples;
 AudioFileReader? local;ProcessCapture? capture;
 int sourceGeneration;
 Thread? worker;volatile bool running;
 volatile MixOptions options=new();volatile bool comms;
 volatile bool liveMicrophone;
 public bool LiveMicrophone=>liveMicrophone;
 public void SetLiveMicrophone(Profile profile,bool enabled)
 {
  if(enabled){if(!Routing&&!liveMicrophone)Start(profile,false,true);liveMicrophone=true;}
  else {liveMicrophone=false;if(!Routing)Stop();}
 }
 bool paused,preview,loop;float localGain=1;
 float[]? recording;int recorded;bool recordActive;
 MixProcessor testMixer=new();
 readonly object probeLock=new();bool probing;double probeSquares;long probeSamples;float probePeak;
 public void BeginLevelProbe(){lock(probeLock){probeSquares=0;probeSamples=0;probePeak=0;probing=true;}}
 public (double Rms,float Peak) EndLevelProbe(){lock(probeLock){probing=false;return(Math.Sqrt(probeSquares/Math.Max(1,probeSamples)),probePeak);}} public bool Routing {get;private set;}public bool Running=>running;
 public bool Transmitting {get{lock(gateLock)return gate.Enabled;}}
 public bool IsPreview {get{lock(audioLock)return preview&&local!=null;}}
 public bool IsPlaying {get{lock(audioLock)return local!=null&&!paused;}}
 public bool IsPaused {get{lock(audioLock)return paused;}}
 public bool HasLocalTrack {get{lock(audioLock)return local!=null;}}
 public bool Recording {get{lock(audioLock)return recordActive;}}
 public bool HasRecording {get{lock(audioLock)return recording!=null&&recorded>0&&!recordActive;}}
 public string PlayingName {get;private set;}="Nothing playing";
 public int SourcePid {get;private set;}
 public float AppRms=>mixer.AppRms;public float MusicPeak=>mixer.MusicPeak;public float MusicRms=>mixer.MusicRms;public float MicPeak=>mixer.MicPeak;public float AppPeak=>mixer.AppPeak;public float OutputPeak=>mixer.OutputPeak;public float DuckGain=>mixer.Duck.Gain;
 public bool Limited=>mixer.Limited;
 public event Action<string>? Error;public event Action? TrackEnded;
 public TimeSpan Position {get{lock(audioLock)return local?.CurrentTime??TimeSpan.Zero;}}
 public TimeSpan Duration {get{lock(audioLock)return local?.TotalTime??TimeSpan.Zero;}}
 public void Configure(MixOptions value)=>options=value;
 public void SetComms(bool value)=>comms=value;
 public void Toggle(){lock(gateLock){if(Routing){fade.Reset();gate.Toggle();}}}
 public void Hold(bool pressed){lock(gateLock)gate.Hold(Routing&&pressed);}
 public void Panic(){lock(gateLock){gate.Panic();fade.Reset();outBuffer?.ClearBuffer();}}
 public void SetLoop(bool value){lock(audioLock)loop=value;}
 public void Start(Profile profile,bool route,bool microphoneTest=false)
 {
  Stop();
  try
  {
   enumerator=new();
   MMDevice Get(string id){if(string.IsNullOrWhiteSpace(id))throw new InvalidOperationException("Choose the audio devices in Setup first.");var d=enumerator.GetDevice(id);if(d.State!=DeviceState.Active){d.Dispose();throw new InvalidOperationException("A saved device is disconnected. Refresh devices in Setup.");}devices.Add(d);return d;}
   if(profile.MonitorId==profile.CableId&&!string.IsNullOrEmpty(profile.CableId))throw new InvalidOperationException("Headphones and the virtual cable must be different outputs to prevent feedback.");
   var monitor=Get(profile.MonitorId);monitorBuffer=Buffer(Format);headphones=new(monitor,AudioClientShareMode.Shared,true,40);headphones.Init(monitorBuffer);headphones.PlaybackStopped+=(_,e)=>{if(running&&e.Exception!=null)Fault("Headphone output was lost: "+e.Exception.Message);};
   if(route||microphoneTest)
   {
    var cable=route?Get(profile.CableId):null;
    if(suppliedMicrophone!=null)micSamples=Devices.Stereo48(suppliedMicrophone);
    else
    {
     var mic=Get(profile.MicId);
     if(mic.FriendlyName.Contains("VB-Audio Virtual Cable",StringComparison.OrdinalIgnoreCase)&&(microphoneTest||cable?.FriendlyName.Contains("VB-Audio Virtual Cable",StringComparison.OrdinalIgnoreCase)==true))throw new InvalidOperationException("Choose your physical microphone, not the cable's recording endpoint. Feeding the cable into itself creates feedback.");
     microphone=new WasapiCapture(mic);micBuffer=Buffer(microphone.WaveFormat);micSamples=Devices.Stereo48(micBuffer.ToSampleProvider());
     microphone.DataAvailable+=(_,e)=>Push(micBuffer,e.Buffer,e.BytesRecorded);microphone.RecordingStopped+=(_,e)=>{if(running&&e.Exception!=null)Fault("Microphone was lost: "+e.Exception.Message);};
    }
    if(cable!=null){outBuffer=Buffer(Format);output=new(cable,AudioClientShareMode.Shared,true,40);output.Init(outBuffer);output.PlaybackStopped+=(_,e)=>{if(running&&e.Exception!=null)Fault("Game input output was lost: "+e.Exception.Message);};}
   }
   Routing=route;running=true;headphones.Play();output?.Play();microphone?.StartRecording();
   worker=new Thread(Pump){IsBackground=true,Name="WarMusic audio mixer",Priority=ThreadPriority.AboveNormal};worker.Start();
  }
  catch{Stop();throw;}
 }
 public async Task ConnectSource(int pid)
 {
  if(!Routing)throw new InvalidOperationException("Start the microphone route before connecting an application.");
  if(Recording)throw new InvalidOperationException("Finish the mix test before reconnecting application audio.");
  if(pid==Environment.ProcessId)throw new InvalidOperationException("WarMusic cannot capture itself.");
  DisconnectSource();int generation=sourceGeneration;
  var next=new ProcessCapture();var buffer=Buffer(ProcessCapture.Format);
  next.Data+=(bytes,count)=>Push(buffer,bytes,count);
  next.Failed+=ex=>{lock(sourceLock){if(capture==next){appSamples=null;appBuffer=null;SourcePid=0;}}Error?.Invoke("Application capture unavailable: "+ex.Message);};
  try{await next.StartAsync(pid);bool accept;lock(sourceLock){accept=running&&Routing&&sourceGeneration==generation;if(accept){capture=next;appBuffer=buffer;appSamples=buffer.ToSampleProvider();SourcePid=pid;}}if(!accept){next.Dispose();throw new InvalidOperationException("The route changed while connecting. Connect again when the new route is ready.");}}
  catch{next.Dispose();throw;}
 }
 public void DisconnectSource()
 {
  ProcessCapture? previous;lock(sourceLock){sourceGeneration++;previous=capture;capture=null;appSamples=null;appBuffer=null;SourcePid=0;}previous?.Dispose();
 }
 static BufferedWaveProvider Buffer(WaveFormat format)=>new(format){BufferDuration=TimeSpan.FromMilliseconds(250),DiscardOnBufferOverflow=true,ReadFully=true};
 static void Push(BufferedWaveProvider? buffer,byte[] bytes,int count){if(buffer==null)return;if(buffer.BufferedDuration.TotalMilliseconds>120)buffer.ClearBuffer();buffer.AddSamples(bytes,0,count);}
 public void Play(Sound sound,bool privatePreview,bool normalize)
 {
  if(!Running)throw new InvalidOperationException("Choose headphones and start preview or routing in Setup.");
  if(!privatePreview&&!Routing)throw new InvalidOperationException("Start routing in Setup, or use Private preview.");
  var path=Path.IsPathRooted(sound.Path)?sound.Path:Path.Combine(Services.Store.Root,sound.Path);
  if(!File.Exists(path))throw new FileNotFoundException("This sound file is missing. Import it again.");
  var next=new AudioFileReader(path);ISampleProvider samples;
  try{samples=Devices.Stereo48(next);}catch{next.Dispose();throw;}
  lock(audioLock){local?.Dispose();local=next;localSamples=samples;preview=privatePreview;paused=false;localGain=normalize?sound.NormalizationGain:1;PlayingName=sound.Name;}
 }
 public void Pause(){lock(audioLock){if(local!=null)paused=!paused;}}
 public void StopLocal(){lock(audioLock){local?.Dispose();local=null;localSamples=null;paused=false;preview=false;PlayingName="Nothing playing";}}
 public void Seek(double seconds){lock(audioLock){if(local!=null){local.CurrentTime=TimeSpan.FromSeconds(Math.Clamp(seconds,0,local.TotalTime.TotalSeconds));localSamples=Devices.Stereo48(local);}}}
 public void BeginTest()
 {
  if(!Routing)throw new InvalidOperationException("Start routing before testing the microphone mix.");
  lock(audioLock){recording=new float[48000*2*10];recorded=0;testMixer=new();recordActive=true;}
 }
 public void EndTest(){lock(audioLock)recordActive=false;}
 public void DeleteTest(){lock(audioLock){recordActive=false;recording=null;recorded=0;}}
 public void PreviewTest()
 {
  string path=Path.Combine(Services.Store.Data,"mix-test.wav");
  // Release the prior preview reader before replacing its temporary WAV.
  StopLocal();
  lock(audioLock){if(recordActive||recording==null||recorded==0)throw new InvalidOperationException("Record a short test first.");using var w=new WaveFileWriter(path,Format);w.WriteSamples(recording,0,recorded);}
  Play(new(){Path=path,Name="Private mix test"},true,false);
 }
 void Pump()
 {
  const int count=960;var mic=new float[count];var app=new float[count];var clip=new float[count];var send=new float[count];var monitor=new float[count];var silentApp=new float[count];var testSend=new float[count];var testMonitor=new float[count];var sendBytes=new byte[count*4];var listenBytes=new byte[count*4];var watch=Stopwatch.StartNew();double next=0;
  try
  {
   while(running)
   {
    var remaining=next-watch.Elapsed.TotalMilliseconds;if(remaining>1){Thread.Sleep((int)Math.Min(remaining,5));continue;}next+=10;if(watch.Elapsed.TotalMilliseconds-next>50)next=watch.Elapsed.TotalMilliseconds;
    Array.Clear(mic);Array.Clear(app);Array.Clear(clip);micSamples?.Read(mic,0,count);
    lock(sourceLock)appSamples?.Read(app,0,count);
    bool privateMode,active,ended=false;
    lock(audioLock)
    {
     privateMode=preview;active=local!=null&&!paused;
     if(active&&localSamples!=null)
     {
      int read=localSamples.Read(clip,0,count);
      for(int i=0;i<read;i++)clip[i]*=localGain;
      if(read<count){if(loop&&local!=null){local.Position=0;localSamples=Devices.Stereo48(local);}else{local?.Dispose();local=null;localSamples=null;PlayingName="Nothing playing";ended=true;}}
     }
    }
    bool transmitted;var blockOptions=options with {MonitorMicrophone=liveMicrophone};
    lock(gateLock)
    {
     if(fade.Step(.01f))gate.Set(false);
     blockOptions=blockOptions with {SendGain=blockOptions.SendGain*fade.Gain};
     transmitted=gate.Enabled;
     mixer.Process(mic,app,clip,send,monitor,count,blockOptions,transmitted,privateMode,comms,active);
     lock(probeLock){if(probing){probeSquares+=(double)mixer.AppRms*mixer.AppRms*count;probeSamples+=count;probePeak=Math.Max(probePeak,mixer.AppPeak);}}
     System.Buffer.BlockCopy(send,0,sendBytes,0,sendBytes.Length);Push(outBuffer,sendBytes,sendBytes.Length);
    }
    System.Buffer.BlockCopy(monitor,0,listenBytes,0,listenBytes.Length);Push(monitorBuffer,listenBytes,listenBytes.Length);
    lock(audioLock){if(recordActive&&recording!=null){testMixer.Process(mic,silentApp,clip,testSend,testMonitor,count,blockOptions,transmitted,privateMode,comms,active);int n=Math.Min(count,recording.Length-recorded);Array.Copy(testSend,0,recording,recorded,n);recorded+=n;if(recorded==recording.Length)recordActive=false;}}
    if(ended&&!privateMode)TrackEnded?.Invoke();
   }
  }
  catch(Exception ex){Fault("Audio stopped: "+ex.Message);}
 }
 void Fault(string text){Panic();Routing=false;Error?.Invoke(text);}
 public void Stop()
 {
  liveMicrophone=false;running=false;Routing=false;Panic();worker?.Join();worker=null;DisconnectSource();
  microphone?.StopRecording();microphone?.Dispose();microphone=null;
  output?.Stop();output?.Dispose();output=null;headphones?.Stop();headphones?.Dispose();headphones=null;
  micSamples=null;micBuffer=null;outBuffer=null;monitorBuffer=null;
  foreach(var d in devices)d.Dispose();devices.Clear();enumerator?.Dispose();enumerator=null;StopLocal();EndTest();
 }
 public void Dispose(){Stop();DeleteTest();var p=Path.Combine(Services.Store.Data,"mix-test.wav");if(File.Exists(p))File.Delete(p);}
}



