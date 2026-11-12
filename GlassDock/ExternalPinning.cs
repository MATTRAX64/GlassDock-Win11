using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GlassDock;
public partial class MainWindow {
 async Task CheckAppMenuAndDropAsync() {
  var report=new List<string>();
  try{
   report.Add(CheckExternalPinning());
   report.Add(CheckInternalDrop());
   var anchor=ItemsPanel.Children.OfType<Button>().First(x=>x.Tag is DockItem app&&app.Children is null);
   var item=(DockItem)anchor.Tag;var menu=BuildAppMenu(item);menu.PlacementTarget=anchor;menu.IsOpen=true;await Task.Delay(150);
   if(menu.Items.OfType<MenuItem>().Count()!=4)throw new Exception("Application menu must contain exactly four actions");
   var position=menu.PointToScreen(new Point());var center=anchor.PointToScreen(new Point(anchor.ActualWidth/2,0));
   var screen=ScreenBounds();var physicalWidth=menu.PointToScreen(new Point(menu.ActualWidth,0)).X-position.X;var expected=Math.Clamp(center.X-physicalWidth/2,screen.Left+8,screen.Right-physicalWidth-8);
   if(Math.Abs(position.X-expected)>3)throw new Exception($"Menu alignment {position.X} instead of {expected}; width={menu.ActualWidth}; anchor={center}; {menu.Tag}");
   report.Add("Four-action application menu / centered above icon with screen-edge clamping: PASS");menu.IsOpen=false;
  }catch(Exception error){report.Add("FAIL: "+error);}
  finally{File.WriteAllLines(Path.Combine(Path.GetDirectoryName(savePath)!,"app-menu-test.txt"),report);Close();}
 }
 string CheckInternalDrop() {
  var original=items;
  try {
   var first=new DockItem {Name="First",Path="first-drag-test.exe"};
   var second=new DockItem {Name="Second",Path="second-drag-test.exe"};
   var third=new DockItem {Name="Third",Path="third-drag-test.exe"};
   items=[first,second,third];
   Render();
   UpdateLayout();
   var buttons=ItemsPanel.Children.OfType<Button>().Where(button=>button.Tag is DockItem).ToArray();
   var leftGap=(LayoutCenter(buttons[0])+LayoutCenter(buttons[1]))/2;
   var rightGap=(LayoutCenter(buttons[1])+LayoutCenter(buttons[2]))/2;
   if(FindDockDestination(first,buttons,leftGap)!=(first,false)
    ||FindDockDestination(third,buttons,rightGap)!=(third,false))
    throw new InvalidOperationException("The gap beside an icon unnecessarily shifted it");
   if(FindDockDestination(first,buttons,rightGap)!=(third,false)
    ||FindDockDestination(third,buttons,leftGap)!=(second,false))
    throw new InvalidOperationException("The dock did not resolve the gap between its neighbors");
   if(FindDockDestination(first,buttons,double.PositiveInfinity)!=(third,true)
    ||FindDockDestination(third,buttons,double.NegativeInfinity)!=(first,false))
    throw new InvalidOperationException("The empty ends of the dock did not target the first and last slots");
   var hoverStart=DateTimeOffset.UtcNow;
   if(FolderHoverReady(hoverStart,hoverStart.AddMilliseconds(249))||!FolderHoverReady(hoverStart,hoverStart.AddMilliseconds(250)))
    throw new InvalidOperationException("Folder hover did not wait for 250 ms");
   PreviewDockSwap(first,second,true);
   PreviewDockSwap(first,third,true);
   var sourceButton=ItemsPanel.Children.OfType<Button>().Single(button=>ReferenceEquals(button.Tag,first));
   if(sourceButton.RenderTransform is not System.Windows.Media.TranslateTransform)
    throw new InvalidOperationException("The dragged icon was not animated into the dock slot");
   var sourceOffset=(System.Windows.Media.TranslateTransform)sourceButton.RenderTransform;
   var settledOffset=(double)sourceOffset.GetAnimationBaseValue(System.Windows.Media.TranslateTransform.XProperty);
   var previewTarget=ItemsPanel.Children.OfType<Button>().Single(button=>ReferenceEquals(button.Tag,second));
   if(!IsDockIconCenter(previewTarget,LayoutCenter(previewTarget))||IsDockIconCenter(previewTarget,rightGap))
    throw new InvalidOperationException("Folder hit area overlaps the insertion gap");
   PreviewDockFolder(first,second,previewTarget);
   if(settledOffset<=0||(double)sourceOffset.GetAnimationBaseValue(System.Windows.Media.TranslateTransform.XProperty)!=settledOffset)
    throw new InvalidOperationException("Folder hover reset the dragged icon to its original slot");
   PreviewDockSwap(first,third,true);
   CommitDockPreview(first);
   ClearDragPreview();
   if(!items.SequenceEqual(new[]{second,third,first}))throw new InvalidOperationException("Drop did not commit the last preview");
   PreviewDockSwap(first,second,false);
   CommitDockPreview(first);
   ClearDragPreview();
   if(!items.SequenceEqual(new[]{first,second,third}))throw new InvalidOperationException("Reverse drop did not restore the requested order");
   var folder=new DockItem {Name="Test folder",Children=[first,second],GridSize=3};
   first.Slot=0;
   second.Slot=1;
   items=[folder,third];
   PlaceInSlot(first,folder,1);
   var saved=StateStore.Read<List<DockItem>>(savePath);
   if(first.Slot!=1||second.Slot!=0||saved?[0].Children?.Single(child=>child.Name=="First").Slot!=1)
    throw new InvalidOperationException("Folder slot swap was not persisted");
   PlaceInSlot(first,folder,4);
   var emptySlotSaved=StateStore.Read<List<DockItem>>(savePath)?[0].Children?.Single(child=>child.Name=="First").Slot;
   if(first.Slot!=4||emptySlotSaved!=4)
    throw new InvalidOperationException("The empty folder slot was not persisted");
   UpdateLayout();
   PreviewDockSwap(first,third,true);
   if(incomingPreviewIcon is null||!folder.Children!.Contains(first))
    throw new InvalidOperationException("Folder extraction preview did not keep the original slot until drop");
   ClearDragPreview();
   var folderAnchor=ItemsPanel.Children.OfType<Button>().Single(button=>ReferenceEquals(button.Tag,folder));
   OpenFolderGrid(folder,folderAnchor);
   var firstWindow=folderWindow;
   OpenFolderGrid(folder,folderAnchor);
   if(firstWindow is null||!ReferenceEquals(folderWindow,firstWindow))
    throw new InvalidOperationException("The same folder reopened on a second click");
   var rename=BuildFolderRenameMenu(folder);
   rename.PlacementTarget=folderAnchor;
   rename.IsOpen=true;
   if(rename.IsOpen)throw new InvalidOperationException("The title menu opened over the already open folder");
   firstWindow.Close();

   items=[first,second,third];
   Render();
   var targetButton=ItemsPanel.Children.OfType<Button>().Single(button=>ReferenceEquals(button.Tag,second));
   PreviewDockFolder(first,second,targetButton);
   if(groupingPreview is null||items.Count!=3)throw new InvalidOperationException("Folder hover did not show a temporary preview");
   CommitDockPreview(first);
   if(items[0].Children?.SequenceEqual(new[]{second,first})!=true)
    throw new InvalidOperationException("Dropping on the folder preview did not create the folder");
   var group=items[0];
   var groupButton=ItemsPanel.Children.OfType<Button>().Single(button=>ReferenceEquals(button.Tag,group));
   PreviewDockFolder(third,group,groupButton);
   CommitDockPreview(third);
   if(group.Children?.Contains(third)!=true||items.Contains(third))
    throw new InvalidOperationException("Dropping on an existing folder did not insert the icon");

   foreach(var level in new byte[]{20,50,80,100})
    foreach(var charging in new[]{false,true})
     if(BatteryIcons.Get(level,charging,Ink.Color).Width!=30)throw new InvalidOperationException("Battery SVG did not render");
   return "Dock slot animation, folder preview and drop, persisted folder swap, single folder window and title, and battery SVGs: PASS";
  } finally {
   folderWindow?.Close();
   ClearDragPreview();
   items=original;
   Save();
  }
 }
 static bool IsAppDropPath(string path)=>File.Exists(path)&&Path.GetExtension(path).ToLowerInvariant() is ".lnk" or ".exe" or ".url" or ".appref-ms";
 void InitializeExternalPinning() {
  GlassSurface.AllowDrop=true;
  GlassSurface.PreviewDragOver+=(_,e)=>{
   if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;
   var paths=e.Data.GetData(DataFormats.FileDrop) as string[]??[];
   e.Effects=paths.Any(IsAppDropPath)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;
   if(e.Effects==DragDropEffects.Copy){revealUntil=DateTime.UtcNow.AddSeconds(1);AnimateDock(false);}
  };
  GlassSurface.PreviewDrop+=(_,e)=>{
   if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;
   var paths=e.Data.GetData(DataFormats.FileDrop) as string[]??[];
   var position=e.GetPosition(ItemsPanel).X;var index=items.Count;
   foreach(var button in ItemsPanel.Children.OfType<Button>()){
    if(button.Tag is DockItem item&&items.Contains(item)&&position<button.TranslatePoint(new Point(button.ActualWidth/2,0),ItemsPanel).X){index=items.IndexOf(item);break;}
   }
   var added=PinDroppedApps(paths,index);e.Effects=added>0?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;
  };
 }
 int PinDroppedApps(IEnumerable<string> paths,int insertion) {
  int added=0;
  foreach(var path in paths.Where(IsAppDropPath)) {
   var target=ResolvePath(path);
   if(AllItems(items).Any(x=>x.Children is null&&string.Equals(ResolvePath(x.Path),target,StringComparison.OrdinalIgnoreCase)))continue;
   var stored=path;
   if(!Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase)){
    var folder=Path.Combine(Path.GetDirectoryName(savePath)!,"Shortcuts");Directory.CreateDirectory(folder);
    stored=Path.Combine(folder,Guid.NewGuid().ToString("N")+Path.GetExtension(path));File.Copy(path,stored);
   }
   items.Insert(Math.Clamp(insertion+added,0,items.Count),new DockItem{Name=Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase)?AppDisplayName(path):Path.GetFileNameWithoutExtension(path),Path=stored});added++;
  }
  if(added>0){Save();revealUntil=DateTime.UtcNow.AddSeconds(2);AnimateDock(false);}return added;
 }
 static string AppDisplayName(string path) {
  if(Path.GetFileNameWithoutExtension(path).Equals("steamwebhelper",StringComparison.OrdinalIgnoreCase))return "Steam";
  try{var details=FileVersionInfo.GetVersionInfo(path);if(!string.IsNullOrWhiteSpace(details.ProductName))return details.ProductName.Trim();if(!string.IsNullOrWhiteSpace(details.FileDescription))return details.FileDescription.Trim();}catch{}
  return Path.GetFileNameWithoutExtension(path);
 }
 string CheckExternalPinning() {
  var original=items.ToList();var shortcut=Path.Combine(Path.GetTempPath(),"GlassDock-drop-test-"+Guid.NewGuid().ToString("N")+".lnk");string? stored=null;object? shell=null,link=null;
  try {
   shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);link=((dynamic)shell!).CreateShortcut(shortcut);
   var target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe");
   ((dynamic)link).TargetPath=target;((dynamic)link).Arguments="/d /c exit";((dynamic)link).Save();
   items=items.Where(x=>x.Children is not null||!string.Equals(ResolvePath(x.Path),target,StringComparison.OrdinalIgnoreCase)).ToList();
   if(PinDroppedApps(new[]{shortcut},0)!=1)throw new Exception("Shortcut drop failed");
   stored=items[0].Path;if(stored==shortcut||!string.Equals(ResolvePath(stored),target,StringComparison.OrdinalIgnoreCase))throw new Exception("Shortcut was not preserved at the dropped position");
   if(PinDroppedApps(new[]{shortcut},0)!=0)throw new Exception("Repeated shortcut drop created a duplicate");
   System.Runtime.InteropServices.Marshal.ReleaseComObject(link);link=((dynamic)shell!).CreateShortcut(stored);
   if((string)((dynamic)link).Arguments!="/d /c exit")throw new Exception("Dropped shortcut lost launch arguments");
   return "External shortcut drop / saved position / arguments / duplicate prevention: PASS";
  } finally {
   items=original;Save();if(link is not null)System.Runtime.InteropServices.Marshal.ReleaseComObject(link);if(shell is not null)System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
   if(File.Exists(shortcut))File.Delete(shortcut);if(stored is not null&&File.Exists(stored))File.Delete(stored);
  }
 }
}
