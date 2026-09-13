using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WarMusic.Services;
namespace WarMusic;
public partial class MainWindow:Window
{
 readonly MainViewModel model;System.Windows.Forms.NotifyIcon? tray;OverlayWindow? overlay;bool exiting,testingTray;
 bool Smoke=>Environment.GetCommandLineArgs().Contains("--smoke");
 public MainWindow()
 {
  InitializeComponent();model=new();DataContext=model;
  Loaded+=(_,_)=>{var handle=new WindowInteropHelper(this).Handle;WindowTheme.Apply(handle);model.AttachHotkeys(handle);if(!Smoke){CreateTray();if(model.StartMinimized)Hide();}};
  model.OverlayRequested+=()=>{if(overlay==null){overlay=new(model);overlay.Closed+=(_,_)=>overlay=null;overlay.Show();}else overlay.Close();};
  Closing+=(_,e)=>{if(!exiting&&(!Smoke||testingTray)&&model.CloseToTray){e.Cancel=true;Hide();tray?.ShowBalloonTip(2500,"WarMusic is in the tray","Routing stays active. Right-click the tray icon to open or exit.",System.Windows.Forms.ToolTipIcon.Info);}};
  Closed+=(_,_)=>{overlay?.Close();tray?.Dispose();model.Dispose();};
 }
 internal void VerifyDesktopFeatures()
 {
  bool prior=model.CloseToTray;CreateTray();testingTray=true;model.CloseToTray=true;Close();if(IsVisible)throw new InvalidOperationException("Close-to-tray failed.");OpenWindow();if(!IsVisible)throw new InvalidOperationException("Tray restore failed.");testingTray=false;model.CloseToTray=prior;

  model.OverlayCommand.Execute(null);if(overlay==null||!overlay.IsVisible)throw new InvalidOperationException("Overlay did not open.");overlay.UpdateLayout();
  var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)overlay.ActualWidth,(int)overlay.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(overlay);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=System.IO.File.Create(System.IO.Path.Combine(Store.Root,"docs","screenshots","overlay.png")))encoder.Save(file);
  model.OverlayCommand.Execute(null);if(overlay!=null)throw new InvalidOperationException("Overlay did not close.");
 } void CreateTray()
 {
  tray=new(){Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),Text="WarMusic",Visible=true};
  var menu=new System.Windows.Forms.ContextMenuStrip();
  menu.Items.Add("Open WarMusic",null,(_,_)=>Dispatcher.Invoke(OpenWindow));
  menu.Items.Add("Ready (music muted)",null,(_,_)=>Dispatcher.Invoke(()=>model.ReadyCommand.Execute(null)));
  menu.Items.Add("Enable / mute music",null,(_,_)=>Dispatcher.Invoke(()=>model.ToggleCommand.Execute(null)));
  menu.Items.Add("Show / hide overlay",null,(_,_)=>Dispatcher.Invoke(()=>model.OverlayCommand.Execute(null)));
  menu.Items.Add("Panic · cut music",null,(_,_)=>Dispatcher.Invoke(()=>model.PanicCommand.Execute(null)));
  menu.Items.Add("Exit WarMusic",null,(_,_)=>Dispatcher.Invoke(()=>{exiting=true;Close();}));
  tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(OpenWindow);
 }
 void OpenWindow(){Show();WindowState=WindowState.Normal;Activate();}
 protected override void OnSourceInitialized(EventArgs e){base.OnSourceInitialized(e);WindowTheme.Apply(new WindowInteropHelper(this).Handle);}
 async void OnDrop(object sender,DragEventArgs e){if(e.Data.GetData(DataFormats.FileDrop) is string[] files)await model.Import(files);}
 WarMusic.Models.Sound? ContextSound(object sender)=>(sender as System.Windows.Controls.MenuItem)?.DataContext as WarMusic.Models.Sound;
 void EditorExpanded(object sender,RoutedEventArgs e){if(QueuePanel!=null)QueuePanel.IsExpanded=false;}
 void QueueExpanded(object sender,RoutedEventArgs e){if(SoundEditor!=null)SoundEditor.IsExpanded=false;}
 void SelectTile(object sender,RoutedEventArgs e){if(ContextSound(sender) is {} sound){model.SelectedSound=sound;SoundEditor.IsExpanded=true;}}
 void PreviewTile(object sender,RoutedEventArgs e){if(ContextSound(sender) is {} sound){model.SelectedSound=sound;model.PreviewCommand.Execute(null);}}
 void QueueTile(object sender,RoutedEventArgs e){if(ContextSound(sender) is {} sound){model.SelectedSound=sound;model.QueueCommand.Execute(null);}} void SeekReleased(object sender,MouseButtonEventArgs e)=>model.Seek(SeekBar.Value);
 void OpenCreditLink(object sender,System.Windows.Navigation.RequestNavigateEventArgs e){System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;}
 void SeekPressed(object sender,MouseButtonEventArgs e)=>model.BeginSeek();
 void SeekKeyReleased(object sender,KeyEventArgs e){if(e.Key is Key.Left or Key.Right or Key.Home or Key.End)model.Seek(SeekBar.Value);}
}





