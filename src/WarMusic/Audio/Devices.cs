using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WarMusic.Models;
namespace WarMusic.Audio;

public static class Devices
{
    internal static bool IsVirtualCablePlayback(string name) => name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) || name.Contains("Virtual Cable", StringComparison.OrdinalIgnoreCase);
    public static bool HasVirtualCable(IEnumerable<Device> outputs) => outputs.Any(device => IsVirtualCablePlayback(device.Name));
    public static List<Device> List(DataFlow flow) { using var e = new MMDeviceEnumerator(); var result = new List<Device>(); foreach (var d in e.EnumerateAudioEndPoints(flow, DeviceState.Active)) { using (d) result.Add(new(d.ID, d.FriendlyName)); } return result; }
    public static List<AppSource> Applications()
    {
        var result = new Dictionary<int, AppSource>();
        using var e = new MMDeviceEnumerator();
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) using (d)
        {
            try { var sessions = d.AudioSessionManager.Sessions; for (int i = 0; i < sessions.Count; i++) { using var s = sessions[i]; int pid = (int)s.GetProcessID; if (pid == 0 || pid == Environment.ProcessId) continue; try { using var p = Process.GetProcessById(pid); result[pid] = new(pid, p.ProcessName); } catch { } } } catch { }
        }
        foreach (var p in Process.GetProcesses()) using (p) { try { if (p.Id != Environment.ProcessId && p.MainWindowHandle != IntPtr.Zero) result.TryAdd(p.Id, new(p.Id, p.ProcessName)); } catch { } }
        var spotify = result.Values.FirstOrDefault(a => a.Name.Equals("Spotify", StringComparison.OrdinalIgnoreCase));
        if (spotify != null) { var resolved = ResolveApplication(spotify); foreach (var id in result.Where(x => x.Value.Name.Equals("Spotify", StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToArray()) result.Remove(id); result[resolved.Pid] = resolved; }
        return result.Values.OrderBy(a => a.Name).ThenBy(a => a.Pid).ToList();
    }
    public static AppSource ResolveApplication(AppSource selected)
    {
        // Spotify's window and audio service can be different, unrelated process trees.
        // Capture the process owning its Windows audio session, not an arbitrary matching PID.
        if (!selected.Name.Equals("Spotify", StringComparison.OrdinalIgnoreCase)) return selected;
        AppSource best = selected; float bestScore = -1;
        using var e = new MMDeviceEnumerator();
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) using (d)
        {
            try
            {
                var sessions = d.AudioSessionManager.Sessions; for (int i = 0; i < sessions.Count; i++) using (var session = sessions[i])
                {
                    try
                    {
                        using var process = Process.GetProcessById((int)session.GetProcessID);
                        if (!process.ProcessName.Equals(selected.Name, StringComparison.OrdinalIgnoreCase)) continue;
                        float score = (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive ? 2 : 0) + session.AudioMeterInformation.MasterPeakValue;
                        if (score > bestScore) { bestScore = score; best = new(process.Id, process.ProcessName); }
                    }
                    catch { }
                }
            }
            catch { }
        }
        return best;
    }
    public static ISampleProvider Stereo48(ISampleProvider source)
    {
        if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
        else if (source.WaveFormat.Channels != 2) throw new NotSupportedException("Select a mono or stereo audio device. Surround input is not supported.");
        if (source.WaveFormat.SampleRate != 48000) source = new WdlResamplingSampleProvider(source, 48000);
        return source;
    }
}
