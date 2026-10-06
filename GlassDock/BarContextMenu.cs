using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
namespace GlassDock;
public partial class MainWindow {
 Native.POINT barMenuPoint;
 void InitializeBarContextMenu() {
  var menu=new ContextMenu {Foreground=Ink,Background=new SolidColorBrush(Dark?Color.FromRgb(38,42,54):Color.FromRgb(239,245,253)),Padding=new Thickness(7),MinWidth=260,FontSize=14,HasDropShadow=false,Placement=PlacementMode.Custom};
  StyleDockMenu(menu);
  var settings=new MenuItem {Header="Paramètres",Icon=BarMenuIcon("\uE713")};settings.Click+=OpenSettings;menu.Items.Add(settings);
  var tasks=new MenuItem {Header="Gestionnaire des tâches",Icon=BarMenuIcon("\uE9D9")};tasks.Click+=OpenTaskManager;menu.Items.Add(tasks);
  GlassSurface.PreviewMouseRightButtonDown+=(_,_)=>Native.GetCursorPos(out barMenuPoint);
  menu.CustomPopupPlacementCallback=(popup,target,offset)=>{
   var origin=GlassSurface.PointToScreen(new Point());
   return new[]{new CustomPopupPlacement(new Point(barMenuPoint.X-origin.X-popup.Width/2,barMenuPoint.Y-origin.Y-popup.Height-8),PopupPrimaryAxis.Horizontal)};
  };
  menu.Opened+=(_,_)=>{
   CloseWindowPreview();revealUntil=DateTime.UtcNow.AddSeconds(1);
   menu.Foreground=Ink;menu.Background=new SolidColorBrush(Dark?Color.FromRgb(38,42,54):Color.FromRgb(239,245,253));
   menu.Resources["MenuHover"]=new SolidColorBrush(Dark?Color.FromArgb(35,255,255,255):Color.FromArgb(65,116,151,198));
   menu.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>PlaceBarContextMenu(menu)));
  };
  GlassSurface.ContextMenu=menu;
 }
 TextBlock BarMenuIcon(string glyph)=>new(){Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=18,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center};
 void PlaceBarContextMenu(ContextMenu menu) {
  if(!menu.IsOpen||PresentationSource.FromVisual(menu) is not System.Windows.Interop.HwndSource source||!Native.GetWindowRect(source.Handle,out var popup))return;
  var start=menu.PointToScreen(new Point());var end=menu.PointToScreen(new Point(menu.ActualWidth,menu.ActualHeight));var screen=ScreenBounds();
  var x=Math.Clamp(barMenuPoint.X-(end.X-start.X)/2,screen.Left+8,Math.Max(screen.Left+8,screen.Right-(end.X-start.X)-8));
  var y=Math.Max(screen.Top+8,Math.Min(barMenuPoint.Y,PanelBottom*DpiScale())-8-(end.Y-start.Y));
  Native.SetWindowPos(source.Handle,System.IntPtr.Zero,popup.Left+(int)Math.Round(x-start.X),popup.Top+(int)Math.Round(y-start.Y),0,0,0x15);
 }
}
