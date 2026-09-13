using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WarMusic.Services;
namespace WarMusic;
public sealed class OverlayWindow:Window
{
 readonly MainViewModel model;
 public OverlayWindow(MainViewModel model)
 {
  this.model=model;DataContext=model;Title="WarMusic overlay";Width=330;Height=165;ResizeMode=ResizeMode.NoResize;WindowStyle=WindowStyle.None;Topmost=true;ShowInTaskbar=false;ShowActivated=false;Background=new SolidColorBrush(Color.FromRgb(25,30,25));Foreground=Brushes.White;
  Left=double.IsFinite(model.OverlayLeft)?Math.Clamp(model.OverlayLeft,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-330):80;
  Top=double.IsFinite(model.OverlayTop)?Math.Clamp(model.OverlayTop,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-165):80;
  var panel=new StackPanel{Margin=new Thickness(12)};Content=panel;
  var header=new DockPanel();var close=new Button{Content="×",Padding=new Thickness(6,0,6,0),Margin=new Thickness(0)};close.Click+=(_,_)=>Close();DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
  var drag=new TextBlock{Text="WARMUSIC · drag to move",FontSize=11,Cursor=Cursors.SizeAll,Padding=new Thickness(0,3,0,8)};drag.MouseLeftButtonDown+=(_,e)=>{if(e.ButtonState==MouseButtonState.Pressed)DragMove();};header.Children.Add(drag);panel.Children.Add(header);
  void Line(string binding,int size){var text=new TextBlock{FontSize=size,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,3,0,0)};text.SetBinding(TextBlock.TextProperty,new Binding(binding));panel.Children.Add(text);}
  Line(nameof(model.Transmission),12);Line(nameof(model.Track),14);Line(nameof(model.DuckStatus),11);
  var buttons=new WrapPanel{Margin=new Thickness(0,8,0,0)};var mute=new Button{Command=model.ToggleCommand,Padding=new Thickness(8,4,8,4)};mute.SetBinding(Button.ContentProperty,new Binding(nameof(model.MusicButtonLabel)));buttons.Children.Add(mute);buttons.Children.Add(new Button{Content="Panic",Command=model.PanicCommand,Padding=new Thickness(8,4,8,4)});panel.Children.Add(buttons);
  Closed+=(_,_)=>{model.OverlayLeft=Left;model.OverlayTop=Top;model.Save();};
 }
}
