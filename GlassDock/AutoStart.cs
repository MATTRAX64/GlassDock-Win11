using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace GlassDock;
public partial class MainWindow {
 const string AutoStartKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
 const string StartupApprovedKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
 const string StartupFolderApprovedKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
 static string StartupShortcutPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"GlassDock.lnk");
 static bool StartupEntryEnabled(string key,string name) {
  using var approved=Registry.CurrentUser.OpenSubKey(key);
  return approved?.GetValue(name) is not byte[] state||state.Length==0||state[0]!=3;
 }
 bool AutoStartEnabled() {
  if(File.Exists(StartupShortcutPath))return StartupEntryEnabled(StartupFolderApprovedKey,"GlassDock.lnk");
  using var run=Registry.CurrentUser.OpenSubKey(AutoStartKey);
  return run?.GetValue("GlassDock") is string&&StartupEntryEnabled(StartupApprovedKey,"GlassDock");
 }
 void MigrateAutoStartShortcut() {
  using var run=Registry.CurrentUser.OpenSubKey(AutoStartKey);
  if(run?.GetValue("GlassDock") is not string)return;
  var enabled=AutoStartEnabled();SetAutoStart(enabled);
 }
 void SetAutoStart(bool enabled) {
  if(enabled) {
   var executable=Path.Combine(Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!,"GlassDock.exe");
   Directory.CreateDirectory(Path.GetDirectoryName(StartupShortcutPath)!);
   object? shell=null,shortcut=null;
   try {
    shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
    shortcut=((dynamic)shell!).CreateShortcut(StartupShortcutPath);
    ((dynamic)shortcut).TargetPath=executable;
    ((dynamic)shortcut).WorkingDirectory=Path.GetDirectoryName(executable)!;
    ((dynamic)shortcut).IconLocation=executable+",0";
    ((dynamic)shortcut).Description="GlassDock";
    ((dynamic)shortcut).Save();
   }finally {if(shortcut is not null&&Marshal.IsComObject(shortcut))Marshal.ReleaseComObject(shortcut);if(shell is not null&&Marshal.IsComObject(shell))Marshal.ReleaseComObject(shell);}
  }else if(File.Exists(StartupShortcutPath))File.Delete(StartupShortcutPath);
  using(var approved=Registry.CurrentUser.OpenSubKey(StartupFolderApprovedKey,true))approved?.DeleteValue("GlassDock.lnk",false);
  // Remove the former Run entry so Windows launches only one instance.
  using(var run=Registry.CurrentUser.OpenSubKey(AutoStartKey,true))run?.DeleteValue("GlassDock",false);
  using(var approved=Registry.CurrentUser.OpenSubKey(StartupApprovedKey,true))approved?.DeleteValue("GlassDock",false);
 }
}
