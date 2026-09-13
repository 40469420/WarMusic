using System.Runtime.InteropServices;
namespace WarMusic.Services;
public static class WindowTheme
{
 public static void Apply(IntPtr window)
 {
  Set(window,20,1); // Immersive dark caption buttons.
  Set(window,35,0x00101310); // COLORREF: caption matches #101310.
  Set(window,36,0x00E8F1EE); // Caption text matches the app.
  Set(window,34,0x00252E23); // Restrained dark border.
 }
 static void Set(IntPtr window,int attribute,int value){int result=DwmSetWindowAttribute(window,attribute,ref value,sizeof(int));if(result<0)Store.Log($"Title bar attribute {attribute}: 0x{result:X8}");}
 public static bool IsApplied(IntPtr window){int caption=0x00101310;int a=DwmSetWindowAttribute(window,35,ref caption,sizeof(int));int b=DwmGetWindowAttribute(window,20,out int dark,sizeof(int));return a==0&&b==0&&dark!=0;}
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
}


