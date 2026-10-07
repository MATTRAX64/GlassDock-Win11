using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
namespace GlassDock;
public partial class MainWindow {
 bool tucked,dragging; DateTime revealUntil; System.Windows.Threading.DispatcherTimer? edgeTimer;
 bool dragDropCommitted;
 double? lastDockAnimationTarget;
 double DockTop(bool hidden) {var info=CurrentMonitorInfo();var bottom=(replacing?info.monitor.Bottom:info.work.Bottom)/DpiScale();return hidden?(fullScreenSuppressed?bottom+1:bottom-8*DockScaleFactor()-5/DpiScale()):bottom-Height-4;}
 void InitializeInteraction() {
  InitializeFullscreenMode();
  InitializeShellPanels();
  InitializeExternalPinning();
  InitializeBarContextMenu();
  InitializeCarousel();
  edgeTimer=new() {Interval=TimeSpan.FromMilliseconds(120)};edgeTimer.Tick+=(_,_)=>UpdateAutoHide();edgeTimer.Start();
  Closed+=(_,_)=>{edgeTimer.Stop();WindowPreviewsClosing();SystemFlyoutsClosing();};
  ItemsPanel.AllowDrop=true;
  ItemsPanel.DragOver+=(_,e)=>{if(e.Handled)return;if(e.Data.GetData(typeof(DockItem)) is DockItem item){var gap=DockGapTarget(item,e.GetPosition(ItemsPanel).X);if(gap.Target is not null)LiveReorder(item,gap.Target,gap.After);e.Effects=DragDropEffects.Move;e.Handled=true;}};
  ItemsPanel.Drop+=(_,e)=>{if(e.Handled)return;if(e.Data.GetData(typeof(DockItem)) is DockItem item){var gap=DockGapTarget(item,e.GetPosition(ItemsPanel).X);MoveItem(item,gap.Target,gap.After,false);e.Effects=DragDropEffects.Move;e.Handled=true;}};
 }
 bool CoveredByApp() {
  var handle=Native.GetForegroundWindow();if(handle==IntPtr.Zero)return false;Native.GetWindowThreadProcessId(handle,out var pid);if(pid==(uint)Environment.ProcessId)return tucked;
  var cls=new System.Text.StringBuilder(256);Native.GetClassName(handle,cls,256);if(cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")return false;
  if(Native.MonitorFromWindow(handle,2)!=Native.MonitorFromWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle,2))return false;
  Native.GetWindowRect(handle,out var rect);var screen=ScreenBounds();return Native.IsZoomed(handle)||(rect.Left<=screen.Left+2&&rect.Top<=screen.Top+2&&rect.Right>=screen.Right-2&&rect.Bottom>=screen.Bottom-2);
 }
 void UpdateAutoHide() {
  PollWindowsKey();
  UpdateHiddenTrayArrow();
  if(replacing)SuppressTaskbar();
  if(!IsVisible||!IsLoaded)return;
  fullScreenSuppressed=replacing&&ForegroundIsFullscreen()&&DateTime.UtcNow>=windowsKeyRevealUntil;
  if(fullScreenSuppressed){CloseWindowPreview();AnimateDock(true);return;}
  EnsureDockTopmost();
  if(!preferences.AutoHide||!replacing){AnimateDock(false);return;}
  Native.GetCursorPos(out var cursor);var screen=ScreenBounds();var dpi=DpiScale();
  var near=cursor.X>=Left*dpi&&cursor.X<=(Left+Width)*dpi&&cursor.Y>=(screen.Bottom-(tucked?8:(Height+10)*dpi));
  bool menuOpen=GlassSurface.ContextMenu?.IsOpen==true||ItemsPanel.Children.OfType<Button>().Any(x=>x.ContextMenu?.IsOpen==true);
  if(near||dragging||menuOpen||IsWindowPreviewOpen||IsSystemFlyoutOpen||IsShellPanelOpen||settingsWindow?.IsActive==true){revealUntil=DateTime.UtcNow.AddMilliseconds(650);AnimateDock(false);return;}
  if(DateTime.UtcNow<revealUntil)return;
  AnimateDock(CoveredByApp());
 }
 void EnsureDockTopmost() {
  if(!IsVisible||fullScreenSuppressed||folderWindow?.IsVisible==true||settingsWindow?.IsVisible==true||utilityPanel?.IsVisible==true||IsWindowPreviewOpen||IsSystemFlyoutOpen||IsShellPanelOpen||NativeTray.IsOverflowVisible()||GlassSurface.ContextMenu?.IsOpen==true||ItemsPanel.Children.OfType<Button>().Any(x=>x.ContextMenu?.IsOpen==true))return;
  Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle,new IntPtr(-1),0,0,0,0,0x0013);
 }
 void AnimateDock(bool hidden) {
  var target=DockTop(hidden);if(tucked==hidden&&lastDockAnimationTarget==target)return;lastDockAnimationTarget=target;tucked=hidden;var current=Top;BeginAnimation(TopProperty,null);Top=target;
  BeginAnimation(TopProperty,new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(240)) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
 }
 void WireDrag(FrameworkElement element,DockItem item) {
  if(element is Button previewButton)WireWindowPreview(previewButton,item);
  if(element is Button hoverButton&&hoverButton.Content is FrameworkElement hoverContent){var scale=new System.Windows.Media.ScaleTransform(1,1);hoverContent.RenderTransformOrigin=new Point(.5,.5);AddCarouselScale(hoverButton,hoverContent,scale);hoverButton.MouseEnter+=(_,_)=>{scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,new DoubleAnimation(1.12,TimeSpan.FromMilliseconds(120)));scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,new DoubleAnimation(1.12,TimeSpan.FromMilliseconds(120)));};hoverButton.MouseLeave+=(_,_)=>{scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(120)));scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(120)));};}
  Point start=new();bool cancelled=false;element.PreviewMouseLeftButtonDown+=(_,e)=>{start=e.GetPosition(element);cancelled=false;dragDropCommitted=false;};
  element.QueryContinueDrag+=(_,e)=>{if(e.EscapePressed)cancelled=true;};
  element.PreviewMouseMove+=(_,e)=>{if(e.LeftButton!=MouseButtonState.Pressed||dragging)return;var point=e.GetPosition(element);if(Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;var backup=items.ToList();var parent=FindParentFolder(item);gapHoverTarget=null;dragging=true;element.Opacity=.3;StartDragGhost(item);try{var result=DragDrop.DoDragDrop(element,new DataObject(typeof(DockItem),item),DragDropEffects.Move);if(result==DragDropEffects.None&&!dragDropCommitted&&!TryExtractOutsideFolder(item,parent,cancelled))items=backup;}finally{if(dragGhost is not null)dragGhost.IsOpen=false;dragGhost=null;element.Opacity=1;dragging=false;Save();revealUntil=DateTime.UtcNow.AddSeconds(1);}e.Handled=true;};
  element.GiveFeedback+=(_,_)=>UpdateDragGhost();
  element.AllowDrop=true;
  element.DragOver+=(_,e)=>{
   if(e.Data.GetData(typeof(DockItem)) is not DockItem source){e.Effects=DragDropEffects.None;return;}
   var x=e.GetPosition(element).X;var group=CanGroup(source,item,x,element.ActualWidth);
   e.Effects=DragDropEffects.Move;
   if(group&&element is Button button){gapHoverTarget=null;button.Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100,100,165,255));}
   else {UpdateAppIndicators();PreviewGapReorder(source,item,x>element.ActualWidth/2);}
   e.Handled=true;
  };
  element.DragLeave+=(_,_)=>UpdateAppIndicators();
  element.Drop+=(_,e)=>{
   if(e.Data.GetData(typeof(DockItem)) is DockItem source&&!ReferenceEquals(source,item)){
    var x=e.GetPosition(element).X;
    if(CanGroup(source,item,x,element.ActualWidth))GroupApps(source,item);
    else MoveItem(source,item,x>element.ActualWidth/2,false);
    e.Effects=DragDropEffects.Move;
   }else e.Effects=e.Data.GetData(typeof(DockItem)) is DockItem existing&&items.Contains(existing)?DragDropEffects.Move:DragDropEffects.None;
   e.Handled=true;
  };
 }
 DateTime lastReorder;
 DockItem? gapHoverTarget;bool gapHoverAfter;DateTime gapHoverSince;
 void PreviewGapReorder(DockItem source,DockItem target,bool after) {
  if(!ReferenceEquals(gapHoverTarget,target)||gapHoverAfter!=after){gapHoverTarget=target;gapHoverAfter=after;gapHoverSince=DateTime.UtcNow;return;}
  if(DateTime.UtcNow-gapHoverSince>=TimeSpan.FromMilliseconds(100))LiveReorder(source,target,after);
 }
 System.Windows.Controls.Primitives.Popup? dragGhost;
 void StartDragGhost(DockItem item) {dragGhost=new() {AllowsTransparency=true,IsHitTestVisible=false,StaysOpen=true,Placement=System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint,Child=new Border {Child=AppIconContent(IconFor(item)),Padding=new Thickness(9),CornerRadius=new CornerRadius(12),Background=new System.Windows.Media.SolidColorBrush(Dark?System.Windows.Media.Color.FromRgb(50,58,72):System.Windows.Media.Color.FromRgb(237,244,255))}};UpdateDragGhost();dragGhost.IsOpen=true;}
 void UpdateDragGhost() {if(dragGhost is null)return;Native.GetCursorPos(out var cursor);dragGhost.HorizontalOffset=cursor.X/DpiScale()+12;dragGhost.VerticalOffset=cursor.Y/DpiScale()+12;}
 void LiveReorder(DockItem source,DockItem target,bool after) {
  if(source==target||!items.Contains(target)||DateTime.UtcNow-lastReorder<TimeSpan.FromMilliseconds(160))return;
  if(!items.Contains(source)) {
   // Open, unpinned apps are appended visually but were missing from the reorder model.
   var runningButton=ItemsPanel.Children.OfType<Button>().FirstOrDefault(x=>x.Tag is RunningApp app&&string.Equals(app.Path,source.Path,StringComparison.OrdinalIgnoreCase));
   if(runningButton is null)return;
   items.Add(source);runningButton.Tag=source;
  }
  var oldIndex=items.IndexOf(source);var targetIndex=items.IndexOf(target);var insertion=targetIndex+(after?1:0);if(oldIndex<insertion)insertion--;if(insertion==oldIndex)return;
  var positions=CapturePositions();items.Remove(source);items.Insert(insertion,source);var sourceButton=ItemsPanel.Children.OfType<Button>().FirstOrDefault(x=>ReferenceEquals(x.Tag,source));if(sourceButton is not null){ItemsPanel.Children.Remove(sourceButton);ItemsPanel.Children.Insert(insertion,sourceButton);}AnimatePositions(positions);lastReorder=DateTime.UtcNow;
 }
 void RemoveFromContainers(DockItem source,List<DockItem> container) {container.Remove(source);foreach(var item in container)if(item.Children is not null)RemoveFromContainers(source,item.Children);}
 void MoveItem(DockItem source,DockItem? target,bool after=false,bool intoFolder=true) {
  if(ReferenceEquals(source,target)||source.Children is not null&&target is not null&&AllItems(source.Children).Contains(target))return;
  if(dragging)dragDropCommitted=true;
  var previousFolder=FindParentFolder(source);
  var entering=intoFolder&&target?.Children is not null&&source.Children is null;
  var origin=TransferOrigin(source,previousFolder);
  RemoveFromContainers(source,items);
  if(entering){source.Slot=FirstFreeSlot(target!);target!.Children!.Add(source);}
  else {int index=target is null?items.Count:items.IndexOf(target);items.Insert(index<0?items.Count:index+(after&&target is not null?1:0),source);source.Slot=-1;}
  if(previousFolder is not null&&(!entering||!ReferenceEquals(previousFolder,target)))DissolveSmallFolder(previousFolder);
  Save();
  if(entering||previousFolder is not null)AnimateFolderTransfer(source,entering?target!:source,origin,entering);
  if(previousFolder is not null&&ReferenceEquals(openedFolder,previousFolder))BuildFolderGrid();
 }
 async void OpenQuickSettings(object sender,RoutedEventArgs e) => await ToggleWindowsQuickSettingsAsync();
}


