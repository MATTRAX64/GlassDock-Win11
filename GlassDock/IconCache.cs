using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
namespace GlassDock;
public partial class MainWindow {
 string CachedIconPath(DockItem item)=>Path.Combine(Path.GetDirectoryName(savePath)!,"Icons",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Path.ToLowerInvariant())))+".png");
 object? ReadCachedIcon(DockItem item){try{var path=CachedIconPath(item);if(!File.Exists(path))return null;var source=new BitmapImage();source.BeginInit();source.UriSource=new Uri(path);source.CacheOption=BitmapCacheOption.OnLoad;source.EndInit();source.Freeze();return new Image{Source=source,Width=28,Height=28};}catch{return null;}}
 void VerifyStartupIcons() {
  shortcutTargets.Clear();shortcutIcons.Clear();int valid=0,missing=0;
  foreach(var item in AllItems(items).Where(x=>x.Children is null)){
   item.Path??="";item.IconPath??="";item.Name??="Application";
   RepairPackageIcon(item);
   try{if(IconFor(item) is Image{Source:BitmapSource source}){
    var path=CachedIconPath(item);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using(var stream=File.Create(path))encoder.Save(stream);valid++;
   }else missing++;}catch{missing++;}
  }
  File.WriteAllText(Path.Combine(Path.GetDirectoryName(savePath)!,"startup-icons.txt"),$"Icônes vérifiées : {valid}; indisponibles : {missing}; éléments conservés : {AllItems(items).Count()}.");
  PersistDock();
  Render();
 }
 void RepairPackageIcon(DockItem item) {
  if(File.Exists(item.IconPath)||!item.Path.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase))return;
  var separator=item.IconPath.IndexOf("\\WindowsApps\\",StringComparison.OrdinalIgnoreCase);if(separator<0)return;
  var relativeStart=item.IconPath.IndexOf('\\',separator+"\\WindowsApps\\".Length);if(relativeStart<0)return;
  var family=item.Path["shell:AppsFolder\\".Length..].Split('!')[0];uint count=0,length=0;
  try {
   PackageIconsNative.GetPackagesByPackageFamily(family,ref count,IntPtr.Zero,ref length,IntPtr.Zero);if(count==0||length==0)return;
   var names=Marshal.AllocHGlobal(checked((int)count*IntPtr.Size));var buffer=Marshal.AllocHGlobal(checked((int)length*2));
   try {
    if(PackageIconsNative.GetPackagesByPackageFamily(family,ref count,names,ref length,buffer)!=0)return;
    for(int index=0;index<count;index++){
     var fullName=Marshal.PtrToStringUni(Marshal.ReadIntPtr(names,index*IntPtr.Size));if(fullName is null)continue;
     uint pathLength=0;PackageIconsNative.GetPackagePathByFullName(fullName,ref pathLength,null);if(pathLength==0)continue;
     var packagePath=new StringBuilder((int)pathLength);if(PackageIconsNative.GetPackagePathByFullName(fullName,ref pathLength,packagePath)!=0)continue;
     var candidate=Path.Combine(packagePath.ToString(),item.IconPath[(relativeStart+1)..]);if(File.Exists(candidate)){item.IconPath=candidate;return;}
    }
   }finally{Marshal.FreeHGlobal(names);Marshal.FreeHGlobal(buffer);}
  }catch{ /* Retain the app and use its saved icon if package metadata is unavailable. */ }
 }
 internal void SaveStateOnExit(){PersistDock();SavePreferences();}
 void PersistDock()=>StateStore.Write(savePath,items);
 async Task CheckStartupStateAsync() {
  var report=new List<string>();try {
   SaveStateOnExit();var read=StateStore.Read<List<DockItem>>(savePath);
   if(System.Text.Json.JsonSerializer.Serialize(read)!=System.Text.Json.JsonSerializer.Serialize(items))throw new Exception("Saved folders/order differ from current layout");
   if(StateStore.Read<Preferences>(PreferencesPath) is null)throw new Exception("Settings missing");
   report.Add("PASS: startup icons checked; layout, folders and settings saved and reloaded");
   if(!Path.GetDirectoryName(savePath)!.Contains(Path.DirectorySeparatorChar+"Tests"+Path.DirectorySeparatorChar))throw new Exception("Tests not isolated");
   report.Add("PASS: test storage isolated from user configuration");
   ReplaceToggle.IsChecked=true;Toggle(this,new RoutedEventArgs());await taskbarToggleTask;
   var tray=await TryOpenHiddenTrayAsync();if(!tray.Success)throw new Exception(tray.Detail);
   if(!(await TryCloseHiddenTrayAsync()).Success)throw new Exception("Hidden icons toggle failed");
   report.Add($"PASS: startup notification button, {tray.IconCount} hidden icons, open/close, Windows taskbar hidden");
  }catch(Exception error){report.Add("FAIL: "+error);}
  finally{File.WriteAllLines(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GlassDock","startup-test.txt"),report);Close();}
 }
}
internal static class PackageIconsNative {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]internal static extern int GetPackagesByPackageFamily(string family,ref uint count,IntPtr fullNames,ref uint bufferLength,IntPtr buffer);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]internal static extern int GetPackagePathByFullName(string name,ref uint length,StringBuilder? path);
}
