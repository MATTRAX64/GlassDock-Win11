using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using System.Runtime.InteropServices;
namespace GlassDock;
public partial class MainWindow {
 Window? folderWindow;DockItem? openedFolder;bool? appliedDark;bool Dark=>preferences.Theme=="Sombre"||(preferences.Theme=="Windows"&&(int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1)??1)==0);
 SolidColorBrush Ink=>new(Dark?Color.FromRgb(240,244,255):Color.FromRgb(30,41,59));
 void ApplyTheme() {appliedDark=Dark;Application.Current.Resources["TooltipBackground"]=new SolidColorBrush(Dark?Color.FromRgb(38,42,54):Color.FromRgb(247,249,253));Application.Current.Resources["TooltipForeground"]=Ink;Application.Current.Resources["TooltipStroke"]=new SolidColorBrush(Dark?Color.FromRgb(89,97,112):Color.FromRgb(198,207,219));Resources["DockHover"]=new SolidColorBrush(Dark?Color.FromArgb(52,255,255,255):Color.FromArgb(150,255,255,255));
  var background=new LinearGradientBrush(Dark?Color.FromRgb(43,48,61):Color.FromRgb(235,241,250),Dark?Color.FromRgb(22,27,37):Color.FromRgb(196,211,232),90);background.Opacity=Math.Clamp(preferences.Opacity,20,100)/100;GlassSurface.Background=background;
  Foreground=Ink;ClockText.Foreground=Ink;DateText.Foreground=Ink;BatteryText.Foreground=Ink;foreach(var button in SystemButtons.Children.OfType<Button>())button.Foreground=Ink;
  GlassSurface.BorderBrush=new SolidColorBrush(Dark?Color.FromArgb(60,255,255,255):Color.FromArgb(160,255,255,255));if(settingsWindow is not null)ApplyWindowTheme(settingsWindow);if(folderWindow is not null){ApplyWindowTheme(folderWindow);BuildFolderGrid();}if(!dragging)Render();
 }
 void ApplyWindowTheme(Window window) {StyleWindow(window);window.Background=new SolidColorBrush(Dark?Color.FromRgb(28,32,42):Color.FromRgb(244,247,252));window.Foreground=Ink;RefreshGlassFrameTheme(window);}
 Dictionary<DockItem,double> CapturePositions() {var result=new Dictionary<DockItem,double>();if(!IsLoaded)return result;foreach(var button in ItemsPanel.Children.OfType<Button>())if(button.Tag is DockItem item)result[item]=button.TranslatePoint(new Point(),ItemsPanel).X;return result;}
 void AnimatePositions(Dictionary<DockItem,double> previous) {if(!IsLoaded)return;ItemsPanel.UpdateLayout();foreach(var button in ItemsPanel.Children.OfType<Button>())if(button.Tag is DockItem item&&previous.TryGetValue(item,out var old)){var delta=old-button.TranslatePoint(new Point(),ItemsPanel).X;if(Math.Abs(delta)>1){var transform=new TranslateTransform();button.RenderTransform=transform;transform.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(delta,0,TimeSpan.FromMilliseconds(220)){EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});}}}
 object FolderIcon(DockItem folder) {
  var border=new Border {Width=34,Height=34,CornerRadius=new CornerRadius(8),Background=new LinearGradientBrush(Color.FromArgb(Dark?(byte)100:(byte)150,160,180,215),Color.FromArgb(Dark?(byte)70:(byte)130,80,105,150),90),BorderBrush=new SolidColorBrush(Color.FromArgb(100,255,255,255)),BorderThickness=new Thickness(1)};var grid=new UniformGrid {Rows=3,Columns=3,Margin=new Thickness(2)};var preview=folder.Children!.OrderBy(x=>x.Slot<0?int.MaxValue:x.Slot).Take(9).ToList();for(int index=0;index<9;index++){var tile=new Border {CornerRadius=new CornerRadius(2),Margin=new Thickness(.6),Background=new SolidColorBrush(Color.FromArgb(24,255,255,255))};if(index<preview.Count){var icon=(FrameworkElement)IconFor(preview[index]);tile.Child=new Viewbox {Child=icon,Stretch=Stretch.Uniform};}grid.Children.Add(tile);}border.Child=grid;return border;
 }
 int folderPage;
 Point folderAnchorScreen;
 DateTime lastFolderPageTurn;
 HashSet<DockItem> knownFolderChildren=new();

 int FirstFreeSlot(DockItem folder) {
  NormalizeSlots(folder);
  var used=folder.Children!.Where(x=>x.Slot>=0).Select(x=>x.Slot).ToHashSet();
  int slot=0;while(used.Contains(slot))slot++;return slot;
 }
 void NormalizeSlots(DockItem folder) {
  // Slots are absolute across pages: 0..8 is page one, 9..17 page two, etc.
  // Reserve all valid positions first so importing new apps cannot displace saved ones.
  folder.GridSize=3;folder.IconSize=Math.Clamp(folder.IconSize,24,56);
  var reserved=folder.Children!.Where(x=>x.Slot>=0).Select(x=>x.Slot).ToHashSet();
  var seen=new HashSet<int>();
  foreach(var child in folder.Children!) {
   if(child.Slot>=0&&seen.Add(child.Slot))continue;
   int free=0;while(reserved.Contains(free))free++;
   child.Slot=free;reserved.Add(free);seen.Add(free);
  }
 }
 int FolderPageCount(DockItem folder) {
  var maximum=folder.Children!.Select(x=>x.Slot).DefaultIfEmpty(-1).Max();
  return Math.Max(1,Math.Max((folder.Children!.Count+8)/9,(maximum+9)/9));
 }
 void ChangeFolderPage(int page) {
  if(openedFolder is null)return;
  var next=Math.Clamp(page,0,FolderPageCount(openedFolder)-1);
  if(next==folderPage)return;
  folderPage=next;lastFolderPageTurn=DateTime.UtcNow;BuildFolderGrid();
 }
 void OpenFolderGrid(DockItem folder,Button anchor) {
  folderWindow?.Close();openedFolder=folder;folderPage=0;NormalizeSlots(folder);
  var dpi=DpiScale();folderAnchorScreen=new Point((Left+Width/2)*dpi,Top*dpi);
  if(anchor.IsLoaded)try {folderAnchorScreen=anchor.PointToScreen(new Point(anchor.ActualWidth/2,0));}catch(InvalidOperationException){}
  // Retain the clicked button until WPF completes its mouse-up routing.
  // Re-rendering here steals activation back from the newly opened folder.
  knownFolderChildren=folder.Children!.ToHashSet();
  var window=new Window {Title=folder.Name,Width=280,Topmost=true,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,SizeToContent=SizeToContent.Height,MaxWidth=SystemParameters.PrimaryScreenWidth-24,MaxHeight=SystemParameters.PrimaryScreenHeight-24};
  folderWindow=window;ApplyWindowTheme(window);
  window.Closed+=(_,_)=>{if(folderWindow==window){folderWindow=null;openedFolder=null;knownFolderChildren.Clear();}};
  bool closing=false;window.Closing+=(_,_)=>closing=true;window.Deactivated+=(_,_)=>{if(!closing&&!dragging)window.Close();};
  window.SizeChanged+=(_,_)=>PositionFolderPopup();window.Loaded+=(_,_)=>PositionFolderPopup();
  window.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Left||e.Key==System.Windows.Input.Key.Right){ChangeFolderPage(folderPage+(e.Key==System.Windows.Input.Key.Left?-1:1));e.Handled=true;}};
  BuildFolderGrid();EnableFolderTitleRename(window,folder);MeasurePanelBeforeShow(window);PositionFolderPopup();window.Show();
 }
 void PositionFolderPopup() {
 if(folderWindow is null||openedFolder is null)return;
  var anchor=ItemsPanel.Children.OfType<Button>().FirstOrDefault(x=>ReferenceEquals(x.Tag,openedFolder)&&x.IsLoaded);
  if(anchor is not null)try {folderAnchorScreen=anchor.PointToScreen(new Point(anchor.ActualWidth/2,0));}catch(InvalidOperationException){}
  var dpi=DpiScale();var screen=ScreenBounds();var width=folderWindow.ActualWidth>0?folderWindow.ActualWidth:folderWindow.Width;var height=folderWindow.ActualHeight>0?folderWindow.ActualHeight:(double.IsNaN(folderWindow.Height)?290:folderWindow.Height);
  var minimumLeft=screen.Left/dpi+6;var maximumLeft=Math.Max(minimumLeft,screen.Right/dpi-width-6);
  var minimumTop=screen.Top/dpi+6;var maximumTop=Math.Max(minimumTop,screen.Bottom/dpi-height-6);
  folderWindow.Left=Math.Clamp(folderAnchorScreen.X/dpi-width/2,minimumLeft,maximumLeft);
  folderWindow.Top=Math.Clamp(Math.Min(folderAnchorScreen.Y/dpi-8,PanelBottom)-height,minimumTop,maximumTop);
 }
 void BuildFolderGrid() {
  if(folderWindow is null||openedFolder is null)return;
  folderIndicatorButtons.Clear();
  var folder=openedFolder;var oldSlots=folder.Children!.Select(x=>x.Slot).ToArray();var oldGrid=folder.GridSize;NormalizeSlots(folder);
  if(oldGrid!=folder.GridSize||!oldSlots.SequenceEqual(folder.Children!.Select(x=>x.Slot)))Save();
  var added=folder.Children!.Where(x=>!knownFolderChildren.Contains(x)).ToList();
  if(added.Count>0)folderPage=added[0].Slot/9;
  knownFolderChildren=folder.Children!.ToHashSet();
  var pages=FolderPageCount(folder);folderPage=Math.Clamp(folderPage,0,pages-1);
  var outer=new StackPanel {Margin=new Thickness(12,12,12,8)};SetGlassWindowBody(folderWindow,outer);
  var grid=new UniformGrid {Rows=3,Columns=3,Width=228,Height=228,HorizontalAlignment=HorizontalAlignment.Center};outer.Children.Add(grid);
  grid.MouseWheel+=(_,e)=>{ChangeFolderPage(folderPage+(e.Delta<0?1:-1));e.Handled=true;};
  for(int index=0;index<9;index++) {
   int slot=folderPage*9+index;
   var normal=Brushes.Transparent;
   var cell=new Border {Margin=new Thickness(3),CornerRadius=new CornerRadius(10),Background=normal,AllowDrop=true};grid.Children.Add(cell);
   var child=folder.Children!.FirstOrDefault(x=>x.Slot==slot);
   if(child is not null) {
    var icon=(FrameworkElement)IconFor(child);icon.Width=Math.Min(40,folder.IconSize);icon.Height=Math.Min(40,folder.IconSize);icon.HorizontalAlignment=HorizontalAlignment.Center;icon.VerticalAlignment=VerticalAlignment.Center;
    var content=new StackPanel {VerticalAlignment=VerticalAlignment.Center};content.Children.Add(icon);
    content.Children.Add(new TextBlock {Text=child.Name,FontSize=9,Foreground=Ink,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=23,MaxWidth=64,Margin=new Thickness(0,4,0,0)});
    content.Children.Add(new Border {Tag="indicator",Height=3,Width=18,CornerRadius=new CornerRadius(2),Margin=new Thickness(0,3,0,0),HorizontalAlignment=HorizontalAlignment.Center,Visibility=Visibility.Hidden});
    var button=new Button {Tag=child,Content=content,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(2),ToolTip=child.Name};
    button.Click+=(_,_)=>Launch(child);WireDrag(button,child);button.ContextMenu=BuildAppMenu(child);cell.Child=button;
    folderIndicatorButtons.Add(button);
   }
   // Tunnel before WireDrag's dock-oriented drop handler so folders keep absolute slots.
   cell.PreviewDragOver+=(_,e)=>{bool accepted=e.Data.GetData(typeof(DockItem)) is DockItem source&&source.Children is null;e.Effects=accepted?DragDropEffects.Move:DragDropEffects.None;cell.Background=accepted?new SolidColorBrush(Dark?Color.FromArgb(48,125,182,255):Color.FromArgb(105,107,161,232)):normal;e.Handled=true;};
   cell.DragLeave+=(_,_)=>cell.Background=normal;
   cell.PreviewDrop+=(_,e)=>{if(e.Data.GetData(typeof(DockItem)) is DockItem source&&source.Children is null){PlaceInSlot(source,folder,slot);BuildFolderGrid();e.Effects=DragDropEffects.Move;}else e.Effects=DragDropEffects.None;e.Handled=true;};
  }
  var dots=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
  var dotsScroll=new ScrollViewer {Content=dots,HorizontalContentAlignment=HorizontalAlignment.Center,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxWidth=216,Margin=new Thickness(0,4,0,0)};outer.Children.Add(dotsScroll);
  void PageDrag(Button button,Func<int> destination) {
   button.AllowDrop=true;button.PreviewDragOver+=(_,e)=>{var valid=e.Data.GetData(typeof(DockItem)) is DockItem item&&item.Children is null;e.Effects=valid?DragDropEffects.Move:DragDropEffects.None;if(valid&&DateTime.UtcNow-lastFolderPageTurn>TimeSpan.FromMilliseconds(650))ChangeFolderPage(destination());e.Handled=true;};
  }
  for(int index=0;index<pages;index++) {
   int page=index;var dot=new Border {Width=page==folderPage?7:5,Height=page==folderPage?7:5,CornerRadius=new CornerRadius(4),Background=new SolidColorBrush(page==folderPage?(Dark?Color.FromRgb(126,184,255):Color.FromRgb(16,102,213)):(Dark?Color.FromRgb(86,96,110):Color.FromRgb(164,175,189)))};
   var button=new Button {Content=dot,Width=18,Height=20,Padding=new Thickness(0),BorderThickness=new Thickness(0),Background=Brushes.Transparent,ToolTip=$"Page {page+1}"};dots.Children.Add(button);button.Click+=(_,_)=>ChangeFolderPage(page);PageDrag(button,()=>page);
  }
  UseCompactGlassFrame(folderWindow);PositionFolderPopup();
  UpdateAppIndicators();
  grid.BeginAnimation(OpacityProperty,new DoubleAnimation(.45,1,TimeSpan.FromMilliseconds(150)));
 }
 void PlaceInSlot(DockItem source,DockItem folder,int slot) {
  if(slot<0||source.Children is not null)return;
  NormalizeSlots(folder);var occupant=folder.Children!.FirstOrDefault(x=>x.Slot==slot);
  if(ReferenceEquals(occupant,source))return;
  if(dragging)dragDropCommitted=true;
  var fromSlot=folder.Children!.Contains(source)?source.Slot:-1;
  var previousFolder=FindParentFolder(source);
  RemoveFromContainers(source,items);
  if(occupant is not null)occupant.Slot=fromSlot>=0?fromSlot:FirstFreeSlot(folder);
  source.Slot=slot;folder.Children!.Add(source);
  if(previousFolder is not null&&!ReferenceEquals(previousFolder,folder))DissolveSmallFolder(previousFolder);
  if(ReferenceEquals(openedFolder,folder))folderPage=slot/9;
  Save();
 }
 readonly Dictionary<IntPtr,Native.RECT> taskbarRects=new();
 Native.WinEventProc? showCallback;IntPtr showHook;
 void InitializeTaskbarGuard() {showCallback=(_,eventId,handle,objectId,childId,thread,time)=>{if(replacing&&objectId==0&&IsTaskbar(handle))SuppressTaskbarWindow(handle);};showHook=Native.SetWinEventHook(0x8002,0x8002,IntPtr.Zero,showCallback,0,0,0);Closed+=(_,_)=>{if(showHook!=IntPtr.Zero)Native.UnhookWinEvent(showHook);};}
 bool IsTaskbar(IntPtr handle) {var cls=new System.Text.StringBuilder(256);Native.GetClassName(handle,cls,256);return cls.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";}
 void SuppressTaskbarWindow(IntPtr handle) {if(NativeTray.IsTransparentTaskbar(handle))return;if(!taskbarRects.ContainsKey(handle)){Native.GetWindowRect(handle,out var rect);taskbarRects[handle]=rect;}Native.EnableWindow(handle,calendarBusy||systemFlyoutAccessInProgress);if(Native.IsWindowVisible(handle)) {Native.SetWindowPos(handle,IntPtr.Zero,0,ScreenBounds().Bottom+200,0,0,0x15);Native.ShowWindow(handle,0);}}
 void SuppressTaskbar() {Native.EnumWindows((handle,_)=>{if(IsTaskbar(handle))SuppressTaskbarWindow(handle);return true;},IntPtr.Zero);}
 void RestoreTaskbar() {foreach(var (handle,rect) in taskbarRects){Native.EnableWindow(handle,true);Native.SetWindowPos(handle,IntPtr.Zero,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top,0x14);Native.ShowWindow(handle,5);}taskbarRects.Clear();}
 void OpenOverflowLegacy(object sender,RoutedEventArgs e) {var handle=Native.FindWindow("TopLevelWindowForOverflowXamlIsland",null);if(handle==IntPtr.Zero)handle=Native.FindWindow("NotifyIconOverflowWindow",null);if(handle!=IntPtr.Zero){Native.SetWindowPos(handle,(IntPtr)(-1),(int)((Left+Width-350)*DpiScale()),(int)((Top-240)*DpiScale()),0,0,0x51);Native.ShowWindow(handle,5);Native.SetForegroundWindow(handle);return;}var menu=new ContextMenu();foreach(var app in FindWindows()){var entry=new MenuItem {Header=app.Name,Icon=WindowIcon(app)};entry.Click+=(_,_)=>Activate(app.Handle);menu.Items.Add(entry);}if(menu.Items.Count==0)menu.Items.Add(new MenuItem {Header="Aucune fenêtre ouverte",IsEnabled=false});menu.IsOpen=true;}
 void UpdateSystemGroup() {BatteryOutline.BorderBrush=Ink;if(Native.GetSystemPowerStatus(out var status)) {BatteryBolt.Visibility=status.ACLineStatus==1?Visibility.Visible:Visibility.Hidden;BatteryFill.Width=16*(status.BatteryLifePercent<=100?status.BatteryLifePercent/100.0:1);BatteryFill.Background=status.ACLineStatus==1?new SolidColorBrush(Color.FromRgb(153,211,159)):Ink;BatteryText.Text=status.BatteryFlag==128?"Secteur":status.BatteryLifePercent==255?"Batterie":$"{status.BatteryLifePercent}%";} }
}









