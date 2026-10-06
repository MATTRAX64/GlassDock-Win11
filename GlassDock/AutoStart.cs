using Microsoft.Win32;
namespace GlassDock;
public partial class MainWindow {
 const string AutoStartKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
 const string StartupApprovedKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
 bool AutoStartEnabled(){using var run=Registry.CurrentUser.OpenSubKey(AutoStartKey);if(run?.GetValue("GlassDock") is not string)return false;using var approved=Registry.CurrentUser.OpenSubKey(StartupApprovedKey);return approved?.GetValue("GlassDock") is not byte[] state||state.Length==0||state[0]!=3;}
 void SetAutoStart(bool enabled){
  using var run=Registry.CurrentUser.CreateSubKey(AutoStartKey);
  if(enabled){var executable=Environment.ProcessPath??System.IO.Path.Combine(AppContext.BaseDirectory,"GlassDock.exe");run.SetValue("GlassDock",$"\"{executable}\"",RegistryValueKind.String);}
  else run.DeleteValue("GlassDock",false);
  using var approved=Registry.CurrentUser.OpenSubKey(StartupApprovedKey,true);approved?.DeleteValue("GlassDock",false);
 }
}
