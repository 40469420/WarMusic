namespace WarMusic.Audio;

public sealed class MusicFade
{
    public float Gain { get; private set; } = 1;
    public bool Active { get; private set; }
    public bool TargetOn { get; private set; }
    float start, elapsed, duration;
    public void Begin(bool on, float seconds, bool currentlyOn) { start = currentlyOn ? Gain : 0; Gain = start; elapsed = 0; duration = Math.Clamp(seconds, .1f, 10); TargetOn = on; Active = true; }
    public void Reset() { Gain = 1; Active = false; }
    public bool Step(float seconds) { if (!Active) return false; elapsed += seconds; float t = Math.Min(1, elapsed / duration); Gain = start + ((TargetOn ? 1 : 0) - start) * t; if (t < 1) return false; Active = false; return !TargetOn; }
}
