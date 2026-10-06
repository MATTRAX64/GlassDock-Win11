using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace GlassDock;
public partial class MainWindow {
 IntPtr lastActiveApp;
 readonly List<Button> folderIndicatorButtons=new();
 Grid AppIconContent(object icon) {var grid=new Grid {Width=36,Height=38};var element=(FrameworkElement)icon;element.VerticalAlignment=VerticalAlignment.Center;element.HorizontalAlignment=HorizontalAlignment.Center;grid.Children.Add(element);grid.Children.Add(new Border {Tag="indicator",Height=3,Width=14,CornerRadius=new CornerRadius(2),Margin=new Thickness(0),VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Center,Visibility=Visibility.Hidden});return grid;}
 bool ItemMatches(DockItem item,RunningApp app)=>AllItems(new[]{item}).Any(child=>child.Children is null&&(MatchesApp(child,app)||string.Equals(ResolvePath(child.Path),app.Path,StringComparison.OrdinalIgnoreCase)));
 void UpdateAppIndicators() {
  var foreground=Native.GetForegroundWindow();Native.GetWindowThreadProcessId(foreground,out var pid);if(pid!=(uint)Environment.ProcessId)lastActiveApp=foreground;
  var buttons=ItemsPanel.Children.OfType<Button>();if(folderWindow?.IsVisible==true)buttons=buttons.Concat(folderIndicatorButtons);
  foreach(var button in buttons) {if(button.Content is not Panel content)continue;var line=content.Children.OfType<Border>().FirstOrDefault(x=>Equals(x.Tag,"indicator"));if(line is null)continue;
   var windows=button.Tag switch {DockItem item=>running.Where(app=>ItemMatches(item,app)).ToList(),RunningApp app=>running.Where(x=>x.Path==app.Path).ToList(),_=>new List<RunningApp>()};var active=windows.Any(x=>x.Handle==lastActiveApp);line.Visibility=windows.Count>0?Visibility.Visible:Visibility.Hidden;line.Width=active?24:18;line.Background=new SolidColorBrush(active?Color.FromRgb(255,132,108):Dark?Color.FromRgb(184,194,209):Color.FromRgb(82,96,117));button.Background=active?new SolidColorBrush(Dark?Color.FromArgb(22,255,255,255):Color.FromArgb(85,255,255,255)):Brushes.Transparent;
  }
 }
}



