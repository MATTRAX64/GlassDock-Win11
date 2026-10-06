using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace GlassDock;

public partial class MainWindow {
 Window? windowPreview;
 Button? previewAnchor;
 DockItem? previewItem;
 DispatcherTimer? previewTimer;
 DateTime previewDue,previewOutsideSince;
 readonly List<PreviewTile> previewTiles=new();
 List<RunningApp> previewApps=new();
 int previewPage;
 int previewRegistrations,previewUpdates;
 bool IsWindowPreviewOpen=>windowPreview is not null;

 sealed class PreviewTile {
  public required RunningApp App;
  public required FrameworkElement Viewport;
  public required TextBlock Placeholder;
  public IntPtr Thumbnail;
 }

 void WireWindowPreview(Button button,DockItem item) {
  if(item.Children is not null)return;
  // A running app gets its live view first; a closed app keeps its ordinary label.
  ToolTipService.SetInitialShowDelay(button,1000);
  ToolTipService.SetBetweenShowDelay(button,0);
  button.MouseEnter+=(_,_)=> {
   if(dragging||Mouse.LeftButton==MouseButtonState.Pressed)return;
   if(!ReferenceEquals(previewAnchor,button))CloseWindowPreview();
   previewAnchor=button;previewItem=item;previewOutsideSince=default;
   previewDue=DateTime.UtcNow.AddMilliseconds(450);
   EnsurePreviewTimer();
  };
  button.MouseLeave+=(_,_)=> {
   if(ReferenceEquals(previewAnchor,button))previewOutsideSince=DateTime.UtcNow;
  };
  button.PreviewMouseLeftButtonDown+=(_,_)=>CloseWindowPreview();
  button.PreviewMouseRightButtonDown+=(_,_)=>CloseWindowPreview();
  button.Unloaded+=(_,_)=> {if(ReferenceEquals(previewAnchor,button))CloseWindowPreview();};
 }

 void EnsurePreviewTimer() {
  if(previewTimer is null) {
   previewTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(75)};
   previewTimer.Tick+=(_,_)=>TickWindowPreview();
  }
  previewTimer.Start();
 }

 void TickWindowPreview() {
  if(previewAnchor is null||!previewAnchor.IsLoaded||!IsVisible||dragging||nativeTrayAccessInProgress) {CloseWindowPreview();return;}
  Native.GetCursorPos(out var cursor);
  bool insideAnchor=ScreenContains(previewAnchor,cursor.X,cursor.Y);
  bool insideFrame=windowPreview is not null&&ScreenContains(windowPreview,cursor.X,cursor.Y);
  // Preserve the frame through a press/release on a thumbnail or its close button.
  if(Mouse.LeftButton==MouseButtonState.Pressed&&!insideFrame) {CloseWindowPreview();return;}
  if(insideAnchor||insideFrame) {
   previewOutsideSince=default;
   revealUntil=DateTime.UtcNow.AddMilliseconds(650);
   if(windowPreview is null&&previewItem is not null&&DateTime.UtcNow>=previewDue) {
    var apps=PreviewWindowsFor(previewItem,previewAnchor);
    if(apps.Count>0)ShowWindowPreview(previewAnchor,previewItem,apps);
    else previewDue=DateTime.UtcNow.AddMilliseconds(800);
   }
   if(windowPreview is not null&&previewApps.All(x=>!PreviewNative.IsWindow(x.Handle)))CloseWindowPreview();
   return;
  }
  if(previewOutsideSince==default)previewOutsideSince=DateTime.UtcNow;
  // The gap above the dock is crossed without losing the preview.
  if(DateTime.UtcNow-previewOutsideSince>=TimeSpan.FromMilliseconds(260))CloseWindowPreview();
 }

 static bool ScreenContains(FrameworkElement element,int x,int y) {
  if(!element.IsVisible||element.ActualWidth<=0||element.ActualHeight<=0)return false;
  try {
   var origin=element.PointToScreen(new Point());
   var dpi=VisualTreeHelper.GetDpi(element);
   return x>=origin.X&&x<=origin.X+element.ActualWidth*dpi.DpiScaleX&&y>=origin.Y&&y<=origin.Y+element.ActualHeight*dpi.DpiScaleY;
  }catch(InvalidOperationException){return false;}
 }

 List<RunningApp> PreviewWindowsFor(DockItem item,Button? anchor=null) {
  var windows=FindWindows();
  if(anchor?.Tag is RunningApp runningApp)return windows.Where(x=>string.Equals(x.Path,runningApp.Path,StringComparison.OrdinalIgnoreCase)).ToList();
  var leaves=AllItems(new[]{item}).Where(x=>x.Children is null).ToList();
  return windows.Where(app=>leaves.Any(leaf=> {
   var target=ResolvePath(leaf.Path);
   return string.Equals(target,app.Path,StringComparison.OrdinalIgnoreCase)||string.Equals(target,System.IO.Path.GetFileName(app.Path),StringComparison.OrdinalIgnoreCase)||MatchesApp(leaf,app);
  })).DistinctBy(x=>x.Handle).ToList();
 }

 void ShowWindowPreview(Button anchor,DockItem item,List<RunningApp> apps) {
  if(windowPreview is not null)return;
  AnimateDock(false);revealUntil=DateTime.UtcNow.AddSeconds(1);
  previewAnchor=anchor;previewItem=item;
  previewApps=apps;previewPage=0;previewRegistrations=0;previewUpdates=0;
  // Layered (AllowsTransparency) windows cannot receive DWM's composed previews.
  // Keep a regular, opaque HWND and let Windows round its outer edge.
  var window=new Window {
   Title="Aperçus — "+item.Name,WindowStyle=WindowStyle.None,AllowsTransparency=false,
   ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,
   Background=new SolidColorBrush(Dark?Color.FromRgb(28,34,46):Color.FromRgb(238,245,255)),SizeToContent=SizeToContent.WidthAndHeight,
   UseLayoutRounding=true,SnapsToDevicePixels=true,Focusable=false
  };
  windowPreview=window;
  WindowChrome.SetWindowChrome(window,new WindowChrome {CaptionHeight=0,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(0),UseAeroCaptionButtons=false});
  window.SourceInitialized+=(_,_)=> {
   var hwnd=new WindowInteropHelper(window).Handle;
   int dark=Dark?1:0,round=1,border=unchecked((int)0xfffffffe);
   PreviewNative.DwmSetWindowAttribute(hwnd,20,ref dark,sizeof(int));
   PreviewNative.DwmSetWindowAttribute(hwnd,33,ref round,sizeof(int));
   PreviewNative.DwmSetWindowAttribute(hwnd,34,ref border,sizeof(int));
   var extended=Native.GetWindowLong(hwnd,-20);
   PreviewNative.SetWindowLongPtr(hwnd,-20,(IntPtr)(extended|0x08000000|0x80)); // no-activate, tool window
  };
  window.MouseEnter+=(_,_)=>previewOutsideSince=default;
  window.MouseLeave+=(_,_)=>previewOutsideSince=DateTime.UtcNow;
  window.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){CloseWindowPreview();e.Handled=true;}};
  window.SizeChanged+=(_,_)=> {RoundPreviewOutline(window);PositionWindowPreview(anchor);UpdatePreviewThumbnails();};
  window.Closed+=(_,_)=> {ReleasePreviewThumbnails();if(ReferenceEquals(windowPreview,window))windowPreview=null;};
  BuildPreviewPage();
  MeasurePanelBeforeShow(window);PositionWindowPreview(anchor);window.Show();window.UpdateLayout();RoundPreviewOutline(window);PositionWindowPreview(anchor);UpdatePreviewThumbnails();
  // Keep the standard tooltip from covering a live preview.
  ToolTipService.SetIsEnabled(anchor,false);
 }

 void RoundPreviewOutline(Window window) {
  var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero||!Native.GetWindowRect(handle,out var bounds))return;
  var diameter=(int)Math.Round(36*VisualTreeHelper.GetDpi(window).DpiScaleX);
  var region=PreviewNative.CreateRoundRectRgn(0,0,bounds.Right-bounds.Left+1,bounds.Bottom-bounds.Top+1,diameter,diameter);
  // Windows owns the region after a successful call; DWM thumbnails remain live.
  if(region!=IntPtr.Zero&&PreviewNative.SetWindowRgn(handle,region,true)==0)PreviewNative.DeleteObject(region);
 }

 void BuildPreviewPage() {
  if(windowPreview is null)return;
  ReleasePreviewThumbnails();
  var panel=new StackPanel();
  var page=previewApps.Skip(previewPage*6).Take(6).ToList();
  int columns=Math.Min(3,page.Count);
  var grid=new Grid {Margin=new Thickness(2)};
  for(int column=0;column<columns;column++)grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(238)});
  for(int row=0;row<(page.Count+columns-1)/columns;row++)grid.RowDefinitions.Add(new RowDefinition {Height=new GridLength(190)});
  for(int i=0;i<page.Count;i++) {
   var app=page[i];
   var card=new Grid {Margin=new Thickness(4),Background=Brushes.Transparent,Cursor=Cursors.Hand};
   card.RowDefinitions.Add(new RowDefinition {Height=new GridLength(34)});
   card.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});
   var heading=new DockPanel {Margin=new Thickness(8,0,7,6),LastChildFill=true};
   var close=new Button {Content="×",Width=24,Height=24,Padding=new Thickness(0),ToolTip="Fermer cette fenêtre",Background=Brushes.Transparent,BorderThickness=new Thickness(0),Foreground=Ink,FontSize=18,Focusable=false};
   close.Click+=(_,e)=> {e.Handled=true;Native.PostMessage(app.Handle,0x10,IntPtr.Zero,IntPtr.Zero);CloseWindowPreview();};
   DockPanel.SetDock(close,Dock.Right);heading.Children.Add(close);
   var icon=new Viewbox {Child=(UIElement)WindowIcon(app),Width=16,Height=16,Margin=new Thickness(0,0,7,0)};
   DockPanel.SetDock(icon,Dock.Left);heading.Children.Add(icon);
   heading.Children.Add(new TextBlock {Text=app.Name,FontSize=12,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=app.Name});
   card.Children.Add(heading);
   var placeholder=new TextBlock {Text="Aperçu indisponible",Foreground=Ink,Opacity=.55,FontSize=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
   var viewport=new Border {Margin=new Thickness(7,0,7,8),CornerRadius=new CornerRadius(9),Background=new SolidColorBrush(Dark?Color.FromRgb(22,27,36):Color.FromRgb(218,227,241)),Child=placeholder};
   Grid.SetRow(viewport,1);card.Children.Add(viewport);
   card.MouseLeftButtonUp+=(_,e)=> {if(e.OriginalSource is Button)return;CloseWindowPreview();Activate(app.Handle);e.Handled=true;};
   var border=new Border {CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),BorderBrush=Brushes.Transparent,Child=card};
   border.MouseEnter+=(_,_)=>{border.Background=new SolidColorBrush(Dark?Color.FromArgb(24,255,255,255):Color.FromArgb(120,255,255,255));border.BorderBrush=new SolidColorBrush(Dark?Color.FromArgb(35,255,255,255):Color.FromArgb(55,120,151,191));};
   border.MouseLeave+=(_,_)=>{border.Background=Brushes.Transparent;border.BorderBrush=Brushes.Transparent;};
   Grid.SetColumn(border,i%columns);Grid.SetRow(border,i/columns);grid.Children.Add(border);
   previewTiles.Add(new PreviewTile {App=app,Viewport=viewport,Placeholder=placeholder});
  }
  panel.Children.Add(grid);
  if(previewApps.Count>6) {
   var nav=new DockPanel {Margin=new Thickness(10,4,10,3)};
   var previous=new Button {Content="‹",Width=30,IsEnabled=previewPage>0,Foreground=Ink,Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
   var next=new Button {Content="›",Width=30,IsEnabled=(previewPage+1)*6<previewApps.Count,Foreground=Ink,Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
   DockPanel.SetDock(previous,Dock.Left);DockPanel.SetDock(next,Dock.Right);nav.Children.Add(previous);nav.Children.Add(next);
   nav.Children.Add(new TextBlock {Text=$"{previewApps.Count} fenêtres · {previewPage+1}/{(previewApps.Count+5)/6}",Foreground=Ink,Opacity=.65,FontSize=11,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
   previous.Click+=(_,_)=>ChangePreviewPage(-1);next.Click+=(_,_)=>ChangePreviewPage(1);panel.Children.Add(nav);
  }
  var shell=new Border {
   Padding=new Thickness(7),CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(1),Child=panel,
   BorderBrush=new LinearGradientBrush(Dark?Color.FromArgb(120,231,240,255):Color.FromArgb(240,255,255,255),Dark?Color.FromArgb(26,160,191,229):Color.FromArgb(100,130,154,190),90),
   Background=new LinearGradientBrush(Dark?Color.FromRgb(49,56,74):Color.FromRgb(250,253,255),Dark?Color.FromRgb(23,29,41):Color.FromRgb(220,232,249),90)
  };
  windowPreview.Content=shell;
 }

 void ChangePreviewPage(int direction) {
  previewPage=Math.Clamp(previewPage+direction,0,(previewApps.Count-1)/6);
  BuildPreviewPage();windowPreview?.UpdateLayout();if(previewAnchor is not null)PositionWindowPreview(previewAnchor);UpdatePreviewThumbnails();
 }

 void PositionWindowPreview(Button anchor) {
  if(windowPreview is null||!anchor.IsLoaded)return;
  var point=anchor.PointToScreen(new Point(anchor.ActualWidth/2,0));
  var dpi=VisualTreeHelper.GetDpi(this);
  var screen=ScreenBounds();
  double left=screen.Left/dpi.DpiScaleX+8,right=screen.Right/dpi.DpiScaleX-8;
  windowPreview.Left=Math.Clamp(point.X/dpi.DpiScaleX-(windowPreview.ActualWidth>0?windowPreview.ActualWidth:windowPreview.Width)/2,left,Math.Max(left,right-(windowPreview.ActualWidth>0?windowPreview.ActualWidth:windowPreview.Width)));
  windowPreview.Top=Math.Max(screen.Top/dpi.DpiScaleY+8,Math.Min(point.Y/dpi.DpiScaleY-12,PanelBottom)-(windowPreview.ActualHeight>0?windowPreview.ActualHeight:windowPreview.Height));
 }

 void UpdatePreviewThumbnails() {
  if(windowPreview is null)return;
  var hwnd=new WindowInteropHelper(windowPreview).Handle;
  if(hwnd==IntPtr.Zero)return;
  var dpi=VisualTreeHelper.GetDpi(windowPreview);
  foreach(var tile in previewTiles) {
   if(tile.Viewport.ActualWidth<=0||tile.Viewport.ActualHeight<=0)continue;
   if(tile.Thumbnail==IntPtr.Zero) {
    if(PreviewNative.DwmRegisterThumbnail(hwnd,tile.App.Handle,out var thumbnail)!=0)continue;
    tile.Thumbnail=thumbnail;previewRegistrations++;
   }
   if(PreviewNative.DwmQueryThumbnailSourceSize(tile.Thumbnail,out var source)!=0||source.Width<=0||source.Height<=0)continue;
   var origin=tile.Viewport.TranslatePoint(new Point(),windowPreview);
   double width=tile.Viewport.ActualWidth,height=tile.Viewport.ActualHeight;
   var scale=Math.Min(width/source.Width,height/source.Height);
   var contentWidth=source.Width*scale;var contentHeight=source.Height*scale;
   double x=origin.X+(width-contentWidth)/2,y=origin.Y+(height-contentHeight)/2;
   var properties=new PreviewNative.ThumbnailProperties {
    Flags=1|4|8|16,Opacity=255,Visible=true,SourceClientAreaOnly=false,
    Destination=new PreviewNative.Rectangle {Left=(int)Math.Round(x*dpi.DpiScaleX),Top=(int)Math.Round(y*dpi.DpiScaleY),Right=(int)Math.Round((x+contentWidth)*dpi.DpiScaleX),Bottom=(int)Math.Round((y+contentHeight)*dpi.DpiScaleY)}
   };
   if(PreviewNative.DwmUpdateThumbnailProperties(tile.Thumbnail,ref properties)==0) {tile.Placeholder.Visibility=Visibility.Collapsed;previewUpdates++;}
  }
 }

 void ReleasePreviewThumbnails() {
  foreach(var tile in previewTiles)if(tile.Thumbnail!=IntPtr.Zero)PreviewNative.DwmUnregisterThumbnail(tile.Thumbnail);
  previewTiles.Clear();
 }

 void CloseWindowPreview() {
  previewTimer?.Stop();
  if(previewAnchor is not null)ToolTipService.SetIsEnabled(previewAnchor,true);
  var window=windowPreview;windowPreview=null;
  ReleasePreviewThumbnails();window?.Close();
  previewAnchor=null;previewItem=null;previewApps.Clear();previewOutsideSince=default;
 }

 void WindowPreviewsClosing()=>CloseWindowPreview();

 // Read-only check for the integration self-test: no activation or close action.
 async Task<string> CheckWindowPreviewsAsync() {
  var app=FindWindows().FirstOrDefault();
  var anchor=ItemsPanel.Children.OfType<Button>().FirstOrDefault();
  if(app is null||anchor is null)return "SKIP: aucune fenêtre disponible pour l’aperçu DWM";
  CloseWindowPreview();
  try {
   ShowWindowPreview(anchor,new DockItem {Name=app.Name,Path=app.Path},new List<RunningApp> {app});
   await Task.Delay(180);UpdatePreviewThumbnails();
   return previewRegistrations>0&&previewUpdates>0?$"PASS: aperçu live DWM ({previewRegistrations} miniature, {previewUpdates} mises à jour)":"FAIL: miniature DWM non enregistrée ou non affichée";
  }finally{CloseWindowPreview();}
 }
}

internal static class PreviewNative {
 [DllImport("gdi32.dll")]internal static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
 [DllImport("user32.dll")]internal static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
 [DllImport("gdi32.dll")]internal static extern bool DeleteObject(IntPtr handle);
 [StructLayout(LayoutKind.Sequential)]internal struct Rectangle {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]internal struct Size {public int Width,Height;}
 [StructLayout(LayoutKind.Sequential)]internal struct Margins {public int Left,Right,Top,Bottom;}
 [StructLayout(LayoutKind.Sequential)]internal struct ThumbnailProperties {
  public uint Flags;
  public Rectangle Destination,Source;
  public byte Opacity;
  [MarshalAs(UnmanagedType.Bool)]public bool Visible;
  [MarshalAs(UnmanagedType.Bool)]public bool SourceClientAreaOnly;
 }
 [DllImport("dwmapi.dll")]internal static extern int DwmRegisterThumbnail(IntPtr destination,IntPtr source,out IntPtr thumbnail);
 [DllImport("dwmapi.dll")]internal static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail,out Size size);
 [DllImport("dwmapi.dll")]internal static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail,ref ThumbnailProperties properties);
 [DllImport("dwmapi.dll")]internal static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
 [DllImport("dwmapi.dll")]internal static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
 [DllImport("dwmapi.dll")]internal static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
 [DllImport("user32.dll")]internal static extern bool IsWindow(IntPtr hwnd);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
}

