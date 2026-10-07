using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GlassDock;
public partial class MainWindow {
 DockItem? FindParentFolder(DockItem item)=>AllItems(items).FirstOrDefault(x=>x.Children?.Contains(item)==true);
 void DissolveSmallFolder(DockItem folder) {
  if(folder.Children is null||folder.Children.Count>1)return;
  var container=FindParentFolder(folder)?.Children??items;
  var index=container.IndexOf(folder);if(index<0)return;
  var remaining=folder.Children.FirstOrDefault();folder.Children.Clear();container.RemoveAt(index);
  if(remaining is not null){remaining.Slot=container==items?-1:folder.Slot;container.Insert(index,remaining);}
  if(ReferenceEquals(openedFolder,folder))folderWindow?.Close();
 }
 bool TryExtractOutsideFolder(DockItem source,DockItem? parent,bool cancelled) {
  if(cancelled||parent?.Children?.Contains(source)!=true||folderWindow is null||!Native.GetCursorPos(out var cursor)||ScreenContains(folderWindow,cursor.X,cursor.Y))return false;
  var point=ItemsPanel.PointFromScreen(new Point(cursor.X,cursor.Y));
  var gap=DockGapTarget(source,point.X);MoveItem(source,gap.Target,gap.After,false);return true;
 }
 static bool CanGroup(DockItem source,DockItem target,double x,double width)=>
  source.Children is null&&!ReferenceEquals(source,target)&&x>=width*.28&&x<=width*.72;
 (DockItem? Target,bool After) DockGapTarget(DockItem source,double x) {
  DockItem? last=null;
  foreach(var button in ItemsPanel.Children.OfType<Button>()) {
   if(button.Tag is not DockItem item||ReferenceEquals(item,source))continue;
   var center=button.TranslatePoint(new Point(button.ActualWidth/2,0),ItemsPanel).X;
   if(x<center)return (item,false);
   last=item;
  }
  return (last,true);
 }

 void GroupApps(DockItem source,DockItem target) {
  if(target.Children is not null){MoveItem(source,target);return;}
  if(dragging)dragDropCommitted=true;
  var sourceParent=FindParentFolder(source);var targetParent=FindParentFolder(target);
  var origin=TransferOrigin(source,null);
  RemoveFromContainers(source,items);
  var index=items.IndexOf(target);
  RemoveFromContainers(target,items);
  var folder=new DockItem {Name="Dossier",Children=new(){target,source},GridSize=3,IconSize=32};
  target.Slot=0;source.Slot=1;
  items.Insert(index<0?items.Count:index,folder);
  if(sourceParent is not null)DissolveSmallFolder(sourceParent);
  if(targetParent is not null&&!ReferenceEquals(targetParent,sourceParent))DissolveSmallFolder(targetParent);
  Save();
  AnimateFolderTransfer(source,folder,origin,true);
  if(openedFolder is not null&&folderWindow is not null)BuildFolderGrid();
 }

 Point TransferOrigin(DockItem source,DockItem? parent) {
  var anchor=ItemsPanel.Children.OfType<Button>().FirstOrDefault(x=>ReferenceEquals(x.Tag,source)||ReferenceEquals(x.Tag,parent));
  if(dragging&&Native.GetCursorPos(out var cursor))return new Point(cursor.X/DpiScale(),cursor.Y/DpiScale());
  if(anchor?.IsLoaded==true){var point=anchor.PointToScreen(new Point(anchor.ActualWidth/2,anchor.ActualHeight/2));return new Point(point.X/DpiScale(),point.Y/DpiScale());}
  return new Point(Left+Width/2,Top+Height/2);
 }

 void AnimateFolderTransfer(DockItem source,DockItem destination,Point origin,bool entering) {
  // A separate surface survives the dock rebuild at the end of DragDrop.
  Dispatcher.BeginInvoke(new Action(()=> {
   if(!IsLoaded)return;
   var button=ItemsPanel.Children.OfType<Button>().FirstOrDefault(x=>ReferenceEquals(x.Tag,destination));
   if(button?.IsLoaded!=true)return;
   var point=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));
   var end=new Point(point.X/DpiScale(),point.Y/DpiScale());
   var scale=new ScaleTransform(entering?1:.25,entering?1:.25);
   var icon=(FrameworkElement)IconFor(source);icon.Width=32;icon.Height=32;
   var surface=new Grid {Width=40,Height=40,IsHitTestVisible=false,RenderTransformOrigin=new Point(.5,.5),RenderTransform=scale};surface.Children.Add(icon);
   var popup=new Popup {Child=surface,AllowsTransparency=true,IsHitTestVisible=false,StaysOpen=true,Placement=PlacementMode.AbsolutePoint,HorizontalOffset=origin.X-20,VerticalOffset=origin.Y-20};popup.IsOpen=true;
   var duration=TimeSpan.FromMilliseconds(280);var ease=new CubicEase {EasingMode=EasingMode.EaseInOut};
   popup.BeginAnimation(Popup.HorizontalOffsetProperty,new DoubleAnimation(origin.X-20,end.X-20,duration){EasingFunction=ease});
   popup.BeginAnimation(Popup.VerticalOffsetProperty,new DoubleAnimation(origin.Y-20,end.Y-20,duration){EasingFunction=ease});
   scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(entering?1:.25,entering?.2:1,duration){EasingFunction=ease});
   scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(entering?1:.25,entering?.2:1,duration){EasingFunction=ease});
   var fade=new DoubleAnimation(entering?1:.4,entering?0:1,duration);fade.Completed+=(_,_)=>popup.IsOpen=false;surface.BeginAnimation(OpacityProperty,fade);
  }),System.Windows.Threading.DispatcherPriority.Loaded);
 }
}
