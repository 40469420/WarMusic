namespace WarMusic.Services;
public static class LevelAdvisor
{
 public static float Recommend(double rms,float peak,float send)
 {
  if(!double.IsFinite(rms)||rms<.0001||!float.IsFinite(peak)||peak<=0)throw new InvalidOperationException("No usable music detected. Start playback and measure again.");
  if(send<.01)throw new InvalidOperationException("Raise Music to game above zero before measuring.");
  return (float)Math.Clamp(Math.Min(.1/(rms*send),.7/(peak*send)),.0631,16);
 }
}
