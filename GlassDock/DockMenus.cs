using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
namespace GlassDock;
public partial class MainWindow {
 ContextMenu BuildAppMenu(DockItem item) {
  if(item.Children is not null)return BuildFolderRenameMenu(item);
  var menu=new ContextMenu {Foreground=Ink,Background=new SolidColorBrush(Dark?Color.FromRgb(38,42,54):Color.FromRgb(239,245,253)),Padding=new Thickness(7),MinWidth=270,Placement=System.Windows.Controls.Primitives.PlacementMode.Custom};
  menu.CustomPopupPlacementCallback=(popup,target,offset)=>new[]{new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point((target.Width-popup.Width)/2,-popup.Height-8),System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)};
  StyleDockMenu(menu);
  void RefreshFolder() {
   if(folderWindow is null||openedFolder is null)return;
   if(!AllItems(items).Any(x=>ReferenceEquals(x,openedFolder))){folderWindow.Close();return;}
   folderWindow.Title=openedFolder.Name;BuildFolderGrid();
  }
  void Add(string text,Action action,bool enabled=true) {var glyph=text.StartsWith("Épingler")?"\uE718":text.StartsWith("Désépingler")?"\uE77A":text.StartsWith("Terminer")?"\uE733":text.StartsWith("Fermer")?"\uE711":"\uE8A7";var entry=new MenuItem {Header=text,IsEnabled=enabled,Foreground=Ink,Icon=new TextBlock {Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=18,VerticalAlignment=VerticalAlignment.Center}};entry.Click+=(_,_)=>{menu.IsOpen=false;action();};menu.Items.Add(entry);}
  menu.Opened+=(_,_)=> {
   if(menu.PlacementTarget is FrameworkElement anchor)menu.PlacementRectangle=new Rect(0,0,anchor.ActualWidth,anchor.ActualHeight);
   menu.Foreground=Ink;menu.Background=new SolidColorBrush(Dark?Color.FromRgb(38,42,54):Color.FromRgb(239,245,253));menu.Resources["MenuHover"]=new SolidColorBrush(Dark?Color.FromArgb(35,255,255,255):Color.FromArgb(65,116,151,198));
   menu.Items.Clear();var windows=ActionWindows(item);
   var title=new MenuItem {Header=item.Name,Icon=new Viewbox {Width=22,Height=22,Child=(UIElement)IconFor(item)},Foreground=Ink};
   title.Click+=(_,_)=>{menu.IsOpen=false;Launch(item);};
   menu.Items.Add(title);
   if(item.Children is null) {
    var pinned=AllItems(items).FirstOrDefault(x=>ReferenceEquals(x,item))??AllItems(items).FirstOrDefault(x=>x.Path==item.Path&&x.Children is null);
    if(pinned is not null) {
     Add("Désépingler de la barre des tâches",()=>{var parent=FindParentFolder(pinned);RemoveFromContainers(pinned,items);if(parent is not null)DissolveSmallFolder(parent);Save();RefreshFolder();});
    } else Add("Épingler à la barre des tâches",()=>{items.Add(new DockItem {Name=item.Name,Path=item.Path,IconPath=item.IconPath});Save();RefreshFolder();});
    var processes=ActionProcesses(item);var hasProcesses=processes.Count>0;foreach(var process in processes)process.Dispose();
    Add("Terminer la tâche",()=>EndAppTasks(item),hasProcesses);
    Add(windows.Count>1?"Fermer toutes les fenêtres":"Fermer la fenêtre",()=>CloseAppWindows(item),windows.Count>0);
   }

   menu.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>CenterAppMenu(menu)));
  };
  return menu;
 }
 void CenterAppMenu(ContextMenu menu) {
  if(!menu.IsOpen||menu.PlacementTarget is not FrameworkElement anchor||System.Windows.PresentationSource.FromVisual(menu) is not System.Windows.Interop.HwndSource source)return;
  if(!Native.GetWindowRect(source.Handle,out var popup))return;
  var origin=menu.PointToScreen(new Point());var end=menu.PointToScreen(new Point(menu.ActualWidth,menu.ActualHeight));
  var icon=anchor.PointToScreen(new Point(anchor.ActualWidth/2,0));var screen=ScreenBounds();
  var x=Math.Clamp(icon.X-(end.X-origin.X)/2,screen.Left+8,Math.Max(screen.Left+8,screen.Right-(end.X-origin.X)-8));
  var y=Math.Max(screen.Top+8,Math.Min(icon.Y,PanelBottom*DpiScale())-8-(end.Y-origin.Y));
  Native.SetWindowPos(source.Handle,IntPtr.Zero,popup.Left+(int)Math.Round(x-origin.X),popup.Top+(int)Math.Round(y-origin.Y),0,0,0x15);
 }
 List<RunningApp> ActionWindows(DockItem item) {
  if(item.Children is not null)return new();var target=ResolvePath(item.Path);var icon=ShortcutIconPath(item.Path);string? package=null;
  const string windowsApps="\\WindowsApps\\";var start=item.IconPath.IndexOf(windowsApps,StringComparison.OrdinalIgnoreCase);if(item.Path.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase)&&start>=0){var end=item.IconPath.IndexOf('\\',start+windowsApps.Length);if(end>=0)package=item.IconPath[..end];}
  return FindWindows().Where(app=>SameExecutable(app.Path,target)||(icon.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&string.Equals(app.Path,icon,StringComparison.OrdinalIgnoreCase))||(package is not null&&app.Path.StartsWith(package+"\\",StringComparison.OrdinalIgnoreCase))).ToList();
 }
 void CloseAppWindows(DockItem item) {foreach(var app in ActionWindows(item))Native.PostMessage(app.Handle,0x10,IntPtr.Zero,IntPtr.Zero);}
 static readonly Dictionary<string,string> executableProducts=new(StringComparer.OrdinalIgnoreCase);
 static string ExecutableProduct(string path) {
  if(executableProducts.TryGetValue(path,out var product))return product;
  try {product=FileVersionInfo.GetVersionInfo(path).ProductName?.Trim()??"";}
  catch(System.IO.IOException) {product="";}
  catch(UnauthorizedAccessException) {product="";}
  executableProducts[path]=product;return product;
 }
 static bool SameExecutable(string executable,string target) {
  if(string.Equals(executable,target,StringComparison.OrdinalIgnoreCase))return true;
  if(!target.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!string.Equals(System.IO.Path.GetFileName(executable),System.IO.Path.GetFileName(target),StringComparison.OrdinalIgnoreCase))return false;
  if(!System.IO.Path.IsPathRooted(target))return true;
  // Launchers and automatic updates can run the same product from another directory.
  var product=ExecutableProduct(target);
  return product.Length>0&&string.Equals(product,ExecutableProduct(executable),StringComparison.OrdinalIgnoreCase);
 }
 List<Process> ActionProcesses(DockItem item) {
  var result=new List<Process>();if(item.Children is not null)return result;
  var target=ResolvePath(item.Path);var icon=ShortcutIconPath(item.Path);
  var windowPids=new HashSet<uint>();foreach(var window in ActionWindows(item)){Native.GetWindowThreadProcessId(window.Handle,out var pid);windowPids.Add(pid);}
  foreach(var process in Process.GetProcesses()) {
   bool matches=false;
   try {
    if(process.Id!=Environment.ProcessId&&!process.HasExited) {
     matches=windowPids.Contains((uint)process.Id);
     if(!matches) {var path=process.MainModule?.FileName;matches=path is not null&&(SameExecutable(path,target)||(icon.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&SameExecutable(path,icon)));}
    }
   }catch(System.ComponentModel.Win32Exception) { }
    catch(InvalidOperationException) { }
   if(matches)result.Add(process);else process.Dispose();
  }
  return result;
 }
 async void EndAppTasks(DockItem item) {
  // Identify processes even when a crashed app no longer has a visible window.
  var processes=ActionProcesses(item);
  var errors=await Task.Run(()=> {
   var failures=new List<string>();
   foreach(var process in processes) {
    try {if(!process.HasExited)process.Kill(entireProcessTree:true);}
    catch(InvalidOperationException) { } // Already exited.
    catch(System.ComponentModel.Win32Exception error) {failures.Add(error.Message);}
    finally {process.Dispose();}
   }
   return failures;
  });
  PollWindows();UpdateAppIndicators();
  if(errors.Count>0)MessageBox.Show(this,string.Join("\n",errors.Distinct()),"Impossible de terminer cette application");
 }
 void StyleDockMenu(ContextMenu menu) {
  menu.Resources["MenuHover"]=new SolidColorBrush(Dark?Color.FromArgb(35,255,255,255):Color.FromArgb(65,116,151,198));
  menu.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse("""
  <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
   <Style TargetType="ContextMenu"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ContextMenu"><Border Background="{TemplateBinding Background}" CornerRadius="14" BorderBrush="#50FFFFFF" BorderThickness="1" Padding="{TemplateBinding Padding}"><StackPanel IsItemsHost="True"/></Border></ControlTemplate></Setter.Value></Setter></Style>
   <Style TargetType="MenuItem"><Setter Property="Foreground" Value="{Binding Foreground,RelativeSource={RelativeSource AncestorType=ContextMenu}}"/><Setter Property="Padding" Value="12,9"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem"><Border x:Name="Row" Background="Transparent" CornerRadius="8" Padding="{TemplateBinding Padding}"><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="30"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions><ContentPresenter ContentSource="Icon" VerticalAlignment="Center"/><ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center"/></Grid></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Row" Property="Background" Value="{DynamicResource MenuHover}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  </ResourceDictionary>
  """));
 }
}

