using System.Windows;
using WarMusic.Services;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace WarMusic;
public partial class App : Application
{
 System.Threading.Mutex? instance;bool ownsInstance;
 protected override void OnExit(ExitEventArgs e){if(ownsInstance)instance?.ReleaseMutex();instance?.Dispose();base.OnExit(e);}
 protected override void OnStartup(StartupEventArgs e)
 {
  if(!e.Args.Contains("--smoke")){instance=new System.Threading.Mutex(true,@"Local\WarMusic.Desktop",out ownsInstance);if(!ownsInstance){MessageBox.Show("WarMusic is already running. Open it from the system tray.","WarMusic");Shutdown();return;}}
  Store.Initialize();
  if(e.Args.Contains("--smoke")){var original=Store.Root;Store.Initialize(Path.Combine(original,"work","ui-smoke"));if(File.Exists(Path.Combine(original,"data","settings.json")))File.Copy(Path.Combine(original,"data","settings.json"),Path.Combine(Store.Data,"settings.json"),true);}
  DispatcherUnhandledException += (_, a) => { Store.Log(a.Exception.ToString()); a.Handled = true; if(e.Args.Contains("--smoke")){Shutdown(1);return;}MessageBox.Show(a.Exception.Message, "WarMusic — something needs attention"); };
  base.OnStartup(e);
  if(e.Args.Contains("--smoke"))
  {
   Dispatcher.BeginInvoke(async ()=>
   {
    try
    {
     await Task.Delay(500);
     var window=(MainWindow)MainWindow;
     var model=(MainViewModel)window.DataContext;
     if(!WindowTheme.IsApplied(new System.Windows.Interop.WindowInteropHelper(window).Handle))throw new InvalidOperationException("Native title bar theme was not applied.");
     Directory.CreateDirectory(Path.Combine(Store.Root,"docs","screenshots"));
     model.Sounds.Add(new(){Name="Convoy ambience",Collection="Arma",Hotkey="Ctrl+1",Color="#365E70"});model.Sounds.Add(new(){Name="Radio check",Collection="Radio",Hotkey="Ctrl+2",Color="#75483C"});
     for(int i=0;i<5;i++)
     {
      model.Tab=i;window.UpdateLayout();await Task.Delay(200);
      var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
      var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
      using var file=File.Create(Path.Combine(Store.Root,"docs","screenshots",$"screen-{i}.png"));encoder.Save(file);
     }
     model.Tab=0;window.Width=1120;window.Height=760;window.SoundEditor.IsExpanded=true;window.UpdateLayout();await Task.Delay(200);
     var compact=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);compact.Render(window);var compactEncoder=new PngBitmapEncoder();compactEncoder.Frames.Add(BitmapFrame.Create(compact));using(var compactFile=File.Create(Path.Combine(Store.Root,"docs","screenshots","compact-editor.png")))compactEncoder.Save(compactFile);
     window.VerifyDesktopFeatures();
     File.WriteAllText(Path.Combine(Store.Root,"docs","ui-smoke.txt"),"All five application tabs rendered. Native Windows dark caption color and dark mode verified.");
     window.Close();
    }
    catch(Exception ex){Store.Log(ex.ToString());Shutdown(1);}
   });
  }
 }
}




