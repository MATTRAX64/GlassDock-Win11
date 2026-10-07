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
