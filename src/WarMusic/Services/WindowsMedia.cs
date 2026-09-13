using Windows.Media.Control;
namespace WarMusic.Services;

public sealed record MediaTrack(string Title,string Artist,string Source,bool Playing,double Position,double Duration,bool CanSeek,bool CanToggle)
{
 public static MediaTrack Empty {get;}=new("Nothing playing","","Windows media",false,0,0,false,false);
}

/// <summary>Reads the same media sessions used by the Windows volume/Now Playing panel.</summary>
public sealed class WindowsMedia
{
 GlobalSystemMediaTransportControlsSessionManager? manager;
 GlobalSystemMediaTransportControlsSession? session;
 bool refreshing;
 public MediaTrack Current {get;private set;}=MediaTrack.Empty;
 public string? LastError {get;private set;}
 public async Task RefreshAsync(string? preferredApplication)
 {
  if(refreshing)return;refreshing=true;
  try
  {
   manager??=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
   var sessions=manager.GetSessions();
   session=SelectSession(sessions,manager.GetCurrentSession(),preferredApplication);
   if(session==null){Current=MediaTrack.Empty;LastError=null;return;}
   var properties=await session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
   var playback=session.GetPlaybackInfo();var timeline=session.GetTimelineProperties();
   bool playing=playback.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
   double duration=Math.Max(0,(timeline.EndTime-timeline.StartTime).TotalSeconds);
   double position=(timeline.Position-timeline.StartTime).TotalSeconds;
   if(playing)position+=Math.Max(0,(DateTimeOffset.UtcNow-timeline.LastUpdatedTime).TotalSeconds)*playback.PlaybackRate.GetValueOrDefault(1);
   string source=session.SourceAppUserModelId;
   if(source.Contains("spotify",StringComparison.OrdinalIgnoreCase))source="Spotify";
   else if(source.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))source=System.IO.Path.GetFileNameWithoutExtension(source);
   Current=new(string.IsNullOrWhiteSpace(properties.Title)?"Untitled media":properties.Title,properties.Artist??"",source,playing,Math.Clamp(position,0,Math.Max(0,duration)),duration,playback.Controls.IsPlaybackPositionEnabled&&duration>0,playback.Controls.IsPlayPauseToggleEnabled);
   LastError=null;
  }
  catch(Exception ex){session=null;Current=MediaTrack.Empty;if(LastError!=ex.Message)Store.Log("Windows media: "+ex.Message);LastError=ex.Message;}
  finally{refreshing=false;}
 }
 static GlobalSystemMediaTransportControlsSession? SelectSession(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions,GlobalSystemMediaTransportControlsSession? current,string? preferred)
 {
  if(!string.IsNullOrWhiteSpace(preferred))
  {
   var matches=sessions.Where(s=>Matches(s.SourceAppUserModelId,preferred)).ToList();
   if(matches.Count>0)return matches.FirstOrDefault(s=>s.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)??matches[0];
  }
  return current??sessions.FirstOrDefault();
 }
 public static bool Matches(string identity,string processName)=>!string.IsNullOrWhiteSpace(processName)&&identity.Contains(System.IO.Path.GetFileNameWithoutExtension(processName),StringComparison.OrdinalIgnoreCase);
 public async Task<bool> ToggleAsync(){var selected=session;return selected!=null&&await selected.TryTogglePlayPauseAsync();}
 public async Task<bool> SeekAsync(double seconds){var selected=session;if(selected==null||!Current.CanSeek)return false;var start=selected.GetTimelineProperties().StartTime;return await selected.TryChangePlaybackPositionAsync((start+TimeSpan.FromSeconds(Math.Clamp(seconds,0,Current.Duration))).Ticks);}
}
