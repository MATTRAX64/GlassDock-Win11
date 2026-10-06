using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
namespace GlassDock;
public partial class MainWindow {
 Window? utilityPanel;
 Window ShowPanel(string title,int width,UIElement content) {
  AnimateDock(false);revealUntil=DateTime.UtcNow.AddSeconds(2);utilityPanel?.Close();var window=new Window {Title=title,Width=width,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.PrimaryScreenHeight-150,Topmost=true,ShowInTaskbar=false};utilityPanel=window;ApplyWindowTheme(window);var body=new StackPanel {Margin=new Thickness(18)};body.Children.Add(content);window.Content=body;ApplyGlassFrame(window);MeasurePanelBeforeShow(window);var screen=ScreenBounds();var dpi=DpiScale();window.Left=Math.Clamp(Left+Width-window.Width-14,screen.Left/dpi+8,Math.Max(screen.Left/dpi+8,screen.Right/dpi-window.Width-8));window.Top=Math.Max(screen.Top/dpi+8,PanelBottom-window.Height);window.Show();
  bool closing=false;window.Closing+=(_,_)=>closing=true;window.Deactivated+=(_,_)=>{if(!dragging&&!closing){closing=true;window.Close();}};window.Closed+=(_,_)=>{if(utilityPanel==window)utilityPanel=null;};return window;
 }
 void OpenAppsPanel(object sender,RoutedEventArgs e) {
  var list=new StackPanel();var apps=FindWindows();foreach(var app in apps){var row=new DockPanel {Margin=new Thickness(0,0,0,10)};var actions=new StackPanel {Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);row.Children.Add(actions);var minimize=new Button {Content="—",ToolTip="Réduire",Padding=new Thickness(8),Margin=new Thickness(4,0,0,0)};minimize.Click+=(_,_)=>Native.ShowWindowAsync(app.Handle,6);actions.Children.Add(minimize);var close=new Button {Content="✕",ToolTip="Fermer normalement",Padding=new Thickness(8),Margin=new Thickness(4,0,0,0)};close.Click+=(_,_)=>{Native.PostMessage(app.Handle,0x10,IntPtr.Zero,IntPtr.Zero);utilityPanel?.Close();};actions.Children.Add(close);var content=new StackPanel {Orientation=Orientation.Horizontal};content.Children.Add((UIElement)WindowIcon(app));content.Children.Add(new TextBlock {Text=app.Name,MaxWidth=215,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center});var open=new Button {Content=content,HorizontalContentAlignment=HorizontalAlignment.Left};open.Click+=(_,_)=>{utilityPanel?.Close();Activate(app.Handle);};row.Children.Add(open);list.Children.Add(row);}if(apps.Count==0)list.Children.Add(new TextBlock {Text="Aucune application ouverte",Foreground=Ink});ShowPanel("Applications ouvertes",440,new ScrollViewer {Content=list,MaxHeight=480,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
 }
 object ClearFolderIcon() {var grid=new Grid {Width=30,Height=28};grid.Children.Add(new System.Windows.Shapes.Path {Data=Geometry.Parse("M2,5 L12,5 L15,9 L28,9 L28,25 L2,25 Z"),Fill=new SolidColorBrush(Color.FromRgb(248,189,62)),Stroke=new SolidColorBrush(Color.FromRgb(225,155,28)),StrokeThickness=1,StrokeLineJoin=PenLineJoin.Round});grid.Children.Add(new Border {Background=new SolidColorBrush(Color.FromRgb(255,208,86)),CornerRadius=new CornerRadius(3),Margin=new Thickness(2,11,2,2)});return grid;}
}




