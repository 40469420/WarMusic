namespace WarMusic.Audio;
public sealed record MixOptions
{
 public float MicGain {get;init;}=1;public float AppGain {get;init;}=.5f;public float ClipGain {get;init;}=.65f;public float SendGain {get;init;}=.65f;public float MonitorGain {get;init;}=.65f;
 public bool DuckEnabled {get;init;}=true;public int DuckMode {get;init;}=0;public float ThresholdDb {get;init;}=-35;public float ReductionDb {get;init;}=15;public float AttackMs {get;init;}=30;public float HoldMs {get;init;}=350;public float ReleaseMs {get;init;}=600;
 public bool DuckMonitor {get;init;}public bool ClipDucking {get;init;}=true;public bool MonitorApplication {get;init;}
 public bool MonitorMicrophone {get;init;}public float MicrophoneMonitorGain {get;init;}=.5f;
}
public sealed class DuckEnvelope
{
 bool voice;float hold;public float Gain {get;private set;}=1;
 public float Step(float level,bool key,MixOptions o,float milliseconds)
 {
  float threshold=MathF.Pow(10,o.ThresholdDb/20);
  voice=level>threshold||(voice&&level>threshold*.70795f);
  bool active=o.DuckEnabled&&(o.DuckMode==0?voice:o.DuckMode==1?key:voice||key);
  if(active)hold=o.HoldMs;else hold=Math.Max(0,hold-milliseconds);
  float target=o.DuckEnabled&&(active||hold>0)?MathF.Pow(10,-o.ReductionDb/20):1;
  float time=target<Gain?o.AttackMs:o.ReleaseMs;
  Gain+=(target-Gain)*(1-MathF.Exp(-milliseconds/Math.Max(1,time)));
  return Gain;
 }
}
public sealed class TransmissionGate
{
 public bool Enabled {get;private set;}
 bool blockedUntilRelease;
 public void Toggle(){Enabled=!Enabled;}
 public void Set(bool value){Enabled=value;}
 public void Panic(){Enabled=false;blockedUntilRelease=true;}
 public void Hold(bool pressed){if(!pressed){blockedUntilRelease=false;Enabled=false;}else if(!blockedUntilRelease)Enabled=true;}
}
public sealed class MixProcessor
{
 public DuckEnvelope Duck {get;}=new();
 float clipEnvelope=1;
 public float MicPeak {get;private set;}public float AppPeak {get;private set;}public float MusicPeak {get;private set;}public float MusicRms {get;private set;}public float AppRms {get;private set;}public float OutputPeak {get;private set;}public bool Limited {get;private set;}
 public void Process(float[] mic,float[] app,float[] clip,float[] send,float[] monitor,int count,MixOptions o,bool transmit,bool preview,bool comms,bool clipActive)
 {
  MicPeak=AppPeak=MusicPeak=OutputPeak=0;Limited=false;double musicSquares=0;double appSquares=0;
  for(int i=0;i<count;i++){appSquares+=(double)app[i]*app[i];MicPeak=Math.Max(MicPeak,Math.Abs(mic[i]*o.MicGain));AppPeak=Math.Max(AppPeak,Math.Abs(app[i]));}
  // Stereo frames: calculate envelopes once per frame, preventing gain mismatch between channels.
  for(int i=0;i<count;i+=2)
  {
   float voice=Math.Max(Math.Abs(mic[i]),Math.Abs(mic[i+1]))*o.MicGain;
   float duck=Duck.Step(voice,comms,o,1000f/48000);
   float clipTarget=o.ClipDucking&&clipActive&&!preview?.35f:1;
   clipEnvelope+=(clipTarget-clipEnvelope)*(1-MathF.Exp(-1f/(48000*(clipTarget<clipEnvelope?.03f:.3f))));
   float appDuck=Math.Min(duck,clipEnvelope);
   for(int ch=0;ch<2;ch++)
   {
    int n=i+ch;float music=app[n]*o.AppGain*appDuck+(preview?0:clip[n]*o.ClipGain*duck);
    float sentMusic=transmit?music*o.SendGain:0;MusicPeak=Math.Max(MusicPeak,Math.Abs(sentMusic));musicSquares+=(double)sentMusic*sentMusic;
    float raw=mic[n]*o.MicGain+sentMusic;
    if(Math.Abs(raw)>.96f)Limited=true;send[n]=Limit(raw);OutputPeak=Math.Max(OutputPeak,Math.Abs(send[n]));
    float listen=(o.MonitorApplication?app[n]*o.AppGain*(o.DuckMonitor?appDuck:1):0)+clip[n]*o.ClipGain*(o.DuckMonitor&&!preview?duck:1);
    monitor[n]=Limit(listen*o.MonitorGain+(o.MonitorMicrophone?mic[n]*o.MicGain*o.MicrophoneMonitorGain:0));
   }
  }
  AppRms=(float)Math.Sqrt(appSquares/Math.Max(1,count));
  MusicRms=(float)Math.Sqrt(musicSquares/Math.Max(1,count));
 }
 public static float Limit(float sample)=>float.IsFinite(sample)?Math.Clamp(sample,-.96f,.96f):0;
 public static float NormalizationGain(double meanSquare,float peak)
 {
  if(meanSquare<1e-8||peak<.0001f)return 1;
  // RMS matching to -20 dBFS, capped at +6 dB; peak protection is a separate stage.
  return (float)Math.Min(Math.Min(.1/Math.Sqrt(meanSquare),2),.95/Math.Max(peak,.0001));
 }
}



