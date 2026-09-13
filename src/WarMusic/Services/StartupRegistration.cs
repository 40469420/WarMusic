using Microsoft.Win32;
namespace WarMusic.Services;
public static class StartupRegistration
{
 const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
 public static bool Enabled {get{using var key=Registry.CurrentUser.OpenSubKey(Key);return key?.GetValue("WarMusic") is string;}}
 public static void Set(bool enabled){using var key=Registry.CurrentUser.CreateSubKey(Key);if(enabled)key.SetValue("WarMusic",$"\"{Environment.ProcessPath}\" --startup");else key.DeleteValue("WarMusic",false);}
}
