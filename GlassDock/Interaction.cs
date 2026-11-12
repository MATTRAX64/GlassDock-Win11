using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
namespace GlassDock;
public partial class MainWindow {
 bool tucked,dragging; DateTime revealUntil; System.Windows.Threading.DispatcherTimer? edgeTimer;
 double? lastDockAnimationTarget;
 double DockTop(bool hidden) {var info=CurrentMonitorInfo();var bottom=(replacing?info.monitor.Bottom:info.work.Bottom)/DpiScale();return hidden?(fullScreenSuppressed?bottom+1:bottom-8*DockScaleFactor()-5/DpiScale()):bottom-Height-4;}
 void InitializeInteraction() {
  InitializeFullscreenMode();
  InitializeWindowDragEffects();
  InitializeShellPanels();
  InitializeExternalPinning();
  InitializeBarContextMenu();
  InitializeCarousel();
  folderHoverTimer.Tick+=(_,_)=>ActivatePendingFolder();
  edgeTimer=new() {Interval=TimeSpan.FromMilliseconds(120)};edgeTimer.Tick+=(_,_)=>UpdateAutoHide();edgeTimer.Start();
  Closed+=(_,_)=>{edgeTimer.Stop();folderHoverTimer.Stop();WindowPreviewsClosing();SystemFlyoutsClosing();};
  ItemsPanel.Background=Brushes.Transparent;
  GlassSurface.PreviewDragOver+=(_,e)=>HandleDockDrag(e,false);
  GlassSurface.PreviewDrop+=(_,e)=>HandleDockDrag(e,true);
  GlassSurface.DragLeave+=(_,e)=>{
   if(!new Rect(GlassSurface.RenderSize).Contains(e.GetPosition(GlassSurface)))ClearDragPreview();
  };
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
  if(near||dragging||menuOpen||IsWindowPreviewOpen||IsSystemFlyoutOpen||IsShellPanelOpen||hiddenTrayPopup?.IsOpen==true||settingsWindow?.IsActive==true){revealUntil=DateTime.UtcNow.AddMilliseconds(650);AnimateDock(false);return;}
  if(DateTime.UtcNow<revealUntil)return;
  AnimateDock(CoveredByApp());
 }
 void EnsureDockTopmost() {
  if(!IsVisible||fullScreenSuppressed||folderWindow?.IsVisible==true||settingsWindow?.IsVisible==true||utilityPanel?.IsVisible==true||IsWindowPreviewOpen||IsSystemFlyoutOpen||IsShellPanelOpen||hiddenTrayPopup?.IsOpen==true||NativeTray.IsOverflowVisible()||GlassSurface.ContextMenu?.IsOpen==true||ItemsPanel.Children.OfType<Button>().Any(x=>x.ContextMenu?.IsOpen==true))return;
  Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle,new IntPtr(-1),0,0,0,0,0x0013);
 }
 void AnimateDock(bool hidden) {
  var target=DockTop(hidden);if(tucked==hidden&&lastDockAnimationTarget==target)return;lastDockAnimationTarget=target;tucked=hidden;var current=Top;BeginAnimation(TopProperty,null);Top=target;
  BeginAnimation(TopProperty,new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(240)) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
 }
 void WireDrag(FrameworkElement element,DockItem item) {
  if(element is Button previewButton)WireWindowPreview(previewButton,item);
  if(element is Button hoverButton&&hoverButton.Content is FrameworkElement hoverContent){var scale=new System.Windows.Media.ScaleTransform(1,1);hoverContent.RenderTransformOrigin=new Point(.5,.5);AddCarouselScale(hoverButton,hoverContent,scale);hoverButton.MouseEnter+=(_,_)=>{scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,new DoubleAnimation(1.12,TimeSpan.FromMilliseconds(120)));scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,new DoubleAnimation(1.12,TimeSpan.FromMilliseconds(120)));};hoverButton.MouseLeave+=(_,_)=>{scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(120)));scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(120)));};}
  if(item.Path.Length==0&&item.Children is null)return;
  Point start=new();
  element.PreviewMouseLeftButtonDown+=(_,e)=>start=e.GetPosition(element);
  element.PreviewMouseMove+=(_,e)=>{
   if(e.LeftButton!=MouseButtonState.Pressed||dragging)return;
   var point=e.GetPosition(element);
   if(Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;
   dragging=true;
   try {
    AnimateDragSheen(1);
    DragDrop.DoDragDrop(element,new DataObject(typeof(DockItem),item),DragDropEffects.Move);
   } finally {
    ClearDragPreview();
    AnimateDragSheen(0);
    UpdateAppIndicators();
    dragging=false;
    revealUntil=DateTime.UtcNow.AddSeconds(1);
   }
   e.Handled=true;
  };
 }
 readonly Dictionary<FrameworkElement,TranslateTransform> dragOffsets=new();
 Button? groupingTarget;
 FrameworkElement? groupingSourceIcon, groupingTargetIcon, groupingPreview;
 Panel? groupingPreviewHost;
 Button? pendingFolderTarget;
 DockItem? pendingFolderSource;
 readonly System.Windows.Threading.DispatcherTimer folderHoverTimer=new(){Interval=TimeSpan.FromMilliseconds(250)};
 DateTimeOffset folderHoverSince;
 const int FolderHoverMilliseconds=250;
 Canvas? incomingPreviewLayer;
 FrameworkElement? incomingPreviewIcon,incomingSourceIcon;

 void MoveDragVisual(FrameworkElement visual, double x, double y) {
  if(!dragOffsets.TryGetValue(visual,out var offset)) {
   offset=visual.RenderTransform is TranslateTransform {IsFrozen:false} previous?previous:new TranslateTransform();
   visual.RenderTransform=offset;
   dragOffsets.Add(visual,offset);
  }
  AnimateDragOffset(offset,TranslateTransform.XProperty,x);
  AnimateDragOffset(offset,TranslateTransform.YProperty,y);
 }

 static void AnimateDragOffset(TranslateTransform offset,DependencyProperty property,double destination) {
  if(Math.Abs((double)offset.GetAnimationBaseValue(property)-destination)<.2)return;
  var current=(double)offset.GetValue(property);
  offset.SetValue(property,destination);
  offset.BeginAnimation(property,new DoubleAnimation(current,destination,TimeSpan.FromMilliseconds(105)){
   EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop
  });
 }

 FrameworkElement? dragPreviewOccupant,dragPreviewFolderSource;
 Point dragPreviewOffset;
 void PreviewFolderSwap(FrameworkElement? source,FrameworkElement? occupant,Point offset) {
  if(ReferenceEquals(source,dragPreviewFolderSource)&&ReferenceEquals(occupant,dragPreviewOccupant)&&offset==dragPreviewOffset)return;
  if(dragPreviewSource is not null)ClearDragPreview();
  if(dragPreviewOccupant is not null&&!ReferenceEquals(dragPreviewOccupant,occupant))
   MoveDragVisual(dragPreviewOccupant,0,0);
  if(dragPreviewFolderSource is not null&&!ReferenceEquals(dragPreviewFolderSource,source))
   MoveDragVisual(dragPreviewFolderSource,0,0);
  dragPreviewFolderSource=source;
  dragPreviewOccupant=occupant;
  dragPreviewOffset=offset;
  if(source is not null)MoveDragVisual(source,-offset.X,-offset.Y);
  if(occupant is not null)MoveDragVisual(occupant,offset.X,offset.Y);
 }

 void ClearDragPreview() {
  foreach(var visual in dragOffsets.Keys.ToArray())MoveDragVisual(visual,0,0);
  dragOffsets.Clear();
  dragPreviewOccupant=null;dragPreviewFolderSource=null;dragPreviewOffset=default;
  dragPreviewSource=null;dragPreviewTarget=null;
  CancelPendingFolder();
  ClearGroupingPreview();
  ClearIncomingPreview();
 }

 void ClearGroupingPreview() {
  if(groupingPreview is not null)groupingPreviewHost?.Children.Remove(groupingPreview);
  if(groupingSourceIcon is not null)groupingSourceIcon.Opacity=1;
  if(groupingTargetIcon is not null)groupingTargetIcon.Opacity=1;
  groupingSourceIcon=null;
  groupingTargetIcon=null;
  groupingPreview=null;
  groupingPreviewHost=null;
  if(groupingTarget is not null){dragPreviewSource=null;dragPreviewTarget=null;}
  groupingTarget=null;
 }

 DockItem? dragPreviewSource,dragPreviewTarget;
 bool dragPreviewAfter;
 void PreviewDockSwap(DockItem source,DockItem? target,bool after) {
  if(groupingTarget is null&&ReferenceEquals(dragPreviewSource,source)&&ReferenceEquals(dragPreviewTarget,target)&&dragPreviewAfter==after)return;
  ClearGroupingPreview();
  dragPreviewSource=source;dragPreviewTarget=target;dragPreviewAfter=after;
  var buttons=ItemsPanel.Children.OfType<Button>().Where(button=>button.Tag is DockItem).ToArray();
  var from=Array.FindIndex(buttons,button=>ReferenceEquals(button.Tag,source));
  var to=Array.FindIndex(buttons,button=>ReferenceEquals(button.Tag,target));
  if(ReferenceEquals(source,target)) {
   foreach(var button in buttons)
    if(dragOffsets.ContainsKey(button))MoveDragVisual(button,0,0);
   ClearIncomingPreview();
   return;
  }
  var insertion=to<0?buttons.Length:to+(after?1:0);
  if(from>=0&&from<insertion)insertion--;
  for(var index=0;index<buttons.Length;index++) {
   var destination=insertion;
   if(index!=from) {
    destination=index;
    if(from>=0&&index>from)destination--;
    if(destination>=insertion)destination++;
   }
   if(destination==index&&!dragOffsets.ContainsKey(buttons[index]))continue;
   var pitch=buttons[index].ActualWidth+buttons[index].Margin.Left+buttons[index].Margin.Right;
   MoveDragVisual(buttons[index],(destination-index)*pitch,0);
  }
  if(from<0)ShowIncomingPreview(source,buttons,insertion);
 }

 void HandleDockDrag(DragEventArgs e,bool drop) {
  if(e.Data.GetData(typeof(DockItem)) is not DockItem source)return;
  var position=e.GetPosition(DockLayout);
  var viewportLeft=IconViewport.TranslatePoint(new Point(),DockLayout).X;
  var viewportRight=viewportLeft+IconViewport.ActualWidth;
  var left=StartButton.IsVisible
   ? Math.Min(viewportLeft,StartButton.TranslatePoint(new Point(StartButton.ActualWidth,0),DockLayout).X):0;
  var right=SystemButtons.TranslatePoint(new Point(),DockLayout).X;
  if(position.X<left||position.X>=right) {
   ClearDragPreview();
   return;
  }

  if(dragPreviewFolderSource is not null||dragPreviewOccupant is not null)ClearDragPreview();
  var pointer=e.GetPosition(ItemsPanel).X;
  var pinned=ItemsPanel.Children.OfType<Button>().Where(candidate=>candidate.Tag is DockItem).ToArray();
  var overViewport=position.X>=viewportLeft&&position.X<=viewportRight;
  var centered=overViewport?pinned.FirstOrDefault(button=>IsDockIconCenter(button,pointer)):null;
  e.Handled=true;
  if(centered is not null) {
   var folderTarget=centered.Tag is DockItem target&&CanGroup(source,target,pointer-VisualTreeHelper.GetOffset(centered).X,centered.ActualWidth)
    ? centered:null;
   if(!ReferenceEquals(pendingFolderTarget,folderTarget)) {
    CancelPendingFolder();
    ClearGroupingPreview();
    if(!drop&&folderTarget is not null) {
     pendingFolderTarget=folderTarget;
     pendingFolderSource=source;
     folderHoverSince=DateTimeOffset.UtcNow;
     folderHoverTimer.Start();
    }
   }
   if(drop&&FolderHoverReady(folderHoverSince,DateTimeOffset.UtcNow))ActivatePendingFolder();
   if(drop) {
    e.Effects=ReferenceEquals(groupingTarget,folderTarget)&&folderTarget is not null?DragDropEffects.Move:DragDropEffects.None;
    if(e.Effects==DragDropEffects.Move)CommitDockPreview(source);
   } else e.Effects=DragDropEffects.Move;
   return;
  }

  CancelPendingFolder();
  var center=overViewport?pointer:position.X<viewportLeft?double.NegativeInfinity:double.PositiveInfinity;
  var (destination,after)=FindDockDestination(source,pinned,center);
  PreviewDockSwap(source,destination,after);
  if(drop)CommitDockPreview(source);
  e.Effects=DragDropEffects.Move;
 }

 void ActivatePendingFolder() {
  folderHoverTimer.Stop();
  if(!dragging||pendingFolderTarget is not {IsLoaded:true,Tag:DockItem target} button
   ||pendingFolderSource is not { } source||!Native.GetCursorPos(out var cursor)
   ||!ScreenContains(IconViewport,cursor.X,cursor.Y))return;
  var pointer=ItemsPanel.PointFromScreen(new Point(cursor.X,cursor.Y)).X;
  if(IsDockIconCenter(button,pointer))PreviewDockFolder(source,target,button);
 }

 void CancelPendingFolder() {
  folderHoverTimer.Stop();
  pendingFolderTarget=null;
  pendingFolderSource=null;
 }

 static bool FolderHoverReady(DateTimeOffset since,DateTimeOffset now) =>
  (now-since).TotalMilliseconds>=FolderHoverMilliseconds;

 static bool IsDockIconCenter(Button button,double x) =>
  Math.Abs(x-LayoutCenter(button))<=button.ActualWidth*.22;

 static (DockItem? Target,bool After) FindDockDestination(DockItem source,Button[] buttons,double center) {
  var gap=Array.FindIndex(buttons,button=>center<LayoutCenter(button));
  if(gap<0)gap=buttons.Length;
  var origin=Array.FindIndex(buttons,button=>ReferenceEquals(button.Tag,source));
  if(origin>=0&&(gap==origin||gap==origin+1))return (source,false);
  if(gap<buttons.Length)return ((DockItem)buttons[gap].Tag,false);
  return (buttons.LastOrDefault()?.Tag as DockItem,true);
 }

 void ShowIncomingPreview(DockItem source,Button[] buttons,int insertion) {
  if(incomingPreviewIcon is null) {
   incomingPreviewLayer=new Canvas {IsHitTestVisible=false};
   Panel.SetZIndex(incomingPreviewLayer,3);
   DockLayout.Children.Add(incomingPreviewLayer);
   incomingPreviewIcon=(FrameworkElement)IconFor(source);
   incomingPreviewIcon.Width=28;
   incomingPreviewIcon.Height=28;
   incomingPreviewLayer.Children.Add(incomingPreviewIcon);
   incomingPreviewIcon.BeginAnimation(OpacityProperty,new DoubleAnimation(.35,1,TimeSpan.FromMilliseconds(105)));
   var folderButton=folderIndicatorButtons.FirstOrDefault(button=>ReferenceEquals(button.Tag,source));
   if(folderButton?.Content is Panel content)incomingSourceIcon=content.Children.OfType<FrameworkElement>().FirstOrDefault();
   if(incomingSourceIcon is not null)incomingSourceIcon.Opacity=.3;
  }
  var first=buttons.FirstOrDefault();
  var pitch=first is null?52:first.ActualWidth+first.Margin.Left+first.Margin.Right;
  var panelCenter=first is null?pitch/2:LayoutCenter(first);
  var point=ItemsPanel.TranslatePoint(new Point(panelCenter+insertion*pitch,ItemsPanel.ActualHeight/2),DockLayout);
  var viewport=IconViewport.TranslatePoint(new Point(),DockLayout);
  incomingPreviewLayer!.Clip=new RectangleGeometry(new Rect(viewport.X,viewport.Y,
   Math.Max(IconViewport.ActualWidth,Math.Min(IconViewport.ActualWidth+pitch,SystemButtons.TranslatePoint(new Point(),DockLayout).X-viewport.X)),IconViewport.ActualHeight));
  Canvas.SetLeft(incomingPreviewIcon,point.X-14);
  Canvas.SetTop(incomingPreviewIcon,point.Y-14);
 }

 void ClearIncomingPreview() {
  if(incomingSourceIcon is not null)incomingSourceIcon.Opacity=1;
  if(incomingPreviewLayer is not null)DockLayout.Children.Remove(incomingPreviewLayer);
  incomingSourceIcon=null;
  incomingPreviewIcon=null;
  incomingPreviewLayer=null;
 }

 static double LayoutCenter(Button button)=>VisualTreeHelper.GetOffset(button).X+button.ActualWidth/2;

 void CommitDockPreview(DockItem source) {
  var target=dragPreviewTarget;
  var after=dragPreviewAfter;
  var group=groupingTarget is not null;
  if(group&&target is not null)GroupApps(source,target);
  else if(!ReferenceEquals(source,target))MoveItem(source,target,after,false);
 }

 void PreviewDockFolder(DockItem source,DockItem target,Button button) {
  if(ReferenceEquals(groupingTarget,button)||button.Content is not Panel host)return;
  ClearGroupingPreview();
  ClearIncomingPreview();
  groupingTarget=button;
  dragPreviewSource=source;
  dragPreviewTarget=target;
  groupingPreviewHost=host;
  // Put the folder preview under the pointer even when its target was shifted by reordering.
  if(dragOffsets.ContainsKey(button))MoveDragVisual(button,0,0);
  groupingTargetIcon=host.Children.OfType<FrameworkElement>().FirstOrDefault();
  var sourceButton=ItemsPanel.Children.OfType<Button>().FirstOrDefault(candidate=>ReferenceEquals(candidate.Tag,source))
   ??folderIndicatorButtons.FirstOrDefault(candidate=>ReferenceEquals(candidate.Tag,source));
  if(sourceButton?.Content is Panel sourceHost)
   groupingSourceIcon=sourceHost.Children.OfType<FrameworkElement>().FirstOrDefault();

  var children=(target.Children??[target]).Select(item=>new DockItem {
   Name=item.Name,Path=item.Path,IconPath=item.IconPath,Slot=item.Slot
  }).ToList();
  children.Add(new DockItem {Name=source.Name,Path=source.Path,IconPath=source.IconPath});
  var folder=new DockItem {Name="Dossier",Children=children,GridSize=3};
  var preview=(FrameworkElement)FolderIcon(folder);
  groupingPreview=preview;
  preview.HorizontalAlignment=HorizontalAlignment.Center;
  preview.VerticalAlignment=VerticalAlignment.Center;
  preview.IsHitTestVisible=false;
  preview.RenderTransformOrigin=new Point(.5,.5);
  var scale=new ScaleTransform(1,1);
  preview.RenderTransform=scale;
  host.Children.Add(preview);
  var duration=TimeSpan.FromMilliseconds(125);
  var ease=new CubicEase {EasingMode=EasingMode.EaseOut};
  if(groupingTargetIcon is not null)groupingTargetIcon.Opacity=0;
  if(groupingSourceIcon is not null)groupingSourceIcon.Opacity=0;
  preview.BeginAnimation(OpacityProperty,new DoubleAnimation(.28,1,duration){FillBehavior=FillBehavior.Stop});
  scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.75,1,duration){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
  scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.75,1,duration){EasingFunction=ease,FillBehavior=FillBehavior.Stop});
 }
 void RemoveFromContainers(DockItem source,List<DockItem> container) {container.Remove(source);foreach(var item in container)if(item.Children is not null)RemoveFromContainers(source,item.Children);}
 void MoveItem(DockItem source,DockItem? target,bool after=false,bool intoFolder=true) {
  if(ReferenceEquals(source,target)||source.Children is not null&&target is not null&&AllItems(source.Children).Contains(target))return;
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


