using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace GlassDock;
public partial class MainWindow {
 IntPtr calendarHandle;
 bool calendarBusy;
 readonly Dictionary<IntPtr,Native.RECT> shellPanelOriginalRects=new();
 readonly Dictionary<uint,string> shellProcessNames=new();
 DispatcherTimer? shellPanelTimer;
 Native.WinEventProc? shellShowCallback;IntPtr shellShowHook;
 double PanelBottom=>(DockTop(false)+8)-8/DpiScale();
 bool IsShellPanelOpen=>FindShellPanels(true).Any();
 void MeasurePanelBeforeShow(Window window) {
  var screen=ScreenBounds();var dpi=DpiScale();
  window.MaxHeight=Math.Max(1,PanelBottom-screen.Top/dpi-8);
  if(window.Content is not FrameworkElement content)return;
  content.Measure(new Size(double.IsNaN(window.Width)?(screen.Right-screen.Left)/dpi-16:window.Width,window.MaxHeight));
  if(double.IsNaN(window.Width))window.Width=Math.Min(content.DesiredSize.Width,(screen.Right-screen.Left)/dpi-16);
  window.Height=Math.Min(content.DesiredSize.Height,window.MaxHeight);
 }

 void InitializeShellPanels() {
  shellPanelTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(120)};
  shellPanelTimer.Tick+=(_,_)=>UpdateShellPanels();shellPanelTimer.Start();
  shellShowCallback=(_,_,window,objectId,_,_,_)=>{if(objectId==0)Dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(()=>{if(IsLoaded&&IsShellPanel(window))PositionShellPanel(window);}));};
  shellShowHook=Native.SetWinEventHook(0x8002,0x8002,IntPtr.Zero,shellShowCallback,0,0,0);
  Closed+=(_,_)=>{shellPanelTimer.Stop();if(shellShowHook!=IntPtr.Zero)Native.UnhookWinEvent(shellShowHook);RestoreShellPanels();NativeTray.ReleaseCalendarAccess();};
 }
 bool IsShellPanel(IntPtr window) {
  if(!Native.GetWindowRect(window,out var bounds)||bounds.Right-bounds.Left<120||bounds.Bottom-bounds.Top<120)return false;
  var cls=new StringBuilder(256);Native.GetClassName(window,cls,256);
  var className=cls.ToString();
  if(className is not ("Windows.UI.Core.CoreWindow" or "ControlCenterWindow")&&!className.StartsWith("XamlExplorerHostIslandWindow",StringComparison.Ordinal))return false;
  Native.GetWindowThreadProcessId(window,out var pid);
  if(!shellProcessNames.TryGetValue(pid,out var process))try{process=Process.GetProcessById((int)pid).ProcessName;shellProcessNames[pid]=process;}catch{return false;}
  return process is "StartMenuExperienceHost" or "SearchHost" or "ShellExperienceHost" or "ShellHost"||(process=="explorer"&&className.StartsWith("XamlExplorerHostIslandWindow",StringComparison.Ordinal));
 }
 IEnumerable<IntPtr> FindShellPanels(bool visible) {
  var result=new HashSet<IntPtr>();
  void Add(IntPtr window){if(window==IntPtr.Zero)return;if(visible){if(!Native.IsWindowVisible(window))return;Native.DwmGetWindowAttribute(window,14,out var cloaked,4);if(cloaked!=0)return;}if(IsShellPanel(window))result.Add(window);}
  Native.EnumWindows((window,_)=>{Add(window);return true;},IntPtr.Zero);
  // Immersive shell surfaces can be absent from EnumWindows on newer builds.
  foreach(var cls in new[]{"Windows.UI.Core.CoreWindow","ControlCenterWindow"}){
   var window=IntPtr.Zero;for(int i=0;i<128;i++){window=Native.FindWindowEx(IntPtr.Zero,window,cls,null);if(window==IntPtr.Zero)break;Add(window);}
  }
  Add(Native.GetForegroundWindow());Add(calendarHandle);return result;
 }
 bool PositionShellPanel(IntPtr window) {
  if(window==positionedSystemFlyout||window==SystemFlyoutNative.LegacyWindow)return PositionSystemFlyout(window);
  if(!Native.GetWindowRect(window,out var rect)||rect.Right<=rect.Left||rect.Bottom<=rect.Top)return false;
  if(!shellPanelOriginalRects.ContainsKey(window))shellPanelOriginalRects[window]=rect;
  var screen=ScreenBounds();var bottom=(int)Math.Floor(PanelBottom*DpiScale());
  var cls=new StringBuilder(256);Native.GetClassName(window,cls,256);
  if(cls.ToString()=="ControlCenterWindow")return true;
  Native.GetWindowThreadProcessId(window,out var pid);
  var process=shellProcessNames.GetValueOrDefault(pid,"");
  var isCalendar=window==calendarHandle||process.Equals("ShellExperienceHost",StringComparison.OrdinalIgnoreCase)||process.Equals("ShellHost",StringComparison.OrdinalIgnoreCase);
  var desiredX=isCalendar?(int)Math.Round((Left+Width-8)*DpiScale())-(rect.Right-rect.Left):rect.Left;
  var x=Math.Clamp(desiredX,screen.Left+8,Math.Max(screen.Left+8,screen.Right-(rect.Right-rect.Left)-8));
  return Native.SetWindowPos(window,new IntPtr(-1),x,bottom-(rect.Bottom-rect.Top),0,0,0x0011);
 }
 void PrepareShellPanels(){foreach(var window in FindShellPanels(false))PositionShellPanel(window);}
 void UpdateShellPanels() {
  if(!IsLoaded)return;
  if(IsVisible)foreach(var window in FindShellPanels(true)){PositionShellPanel(window);revealUntil=DateTime.UtcNow.AddMilliseconds(600);}
  if(calendarHandle!=IntPtr.Zero&&(!Native.IsWindowVisible(calendarHandle)||IsCloaked(calendarHandle))) {calendarHandle=IntPtr.Zero;NativeTray.ReleaseCalendarAccess();}
 }
 static bool IsCloaked(IntPtr window){Native.DwmGetWindowAttribute(window,14,out var cloaked,4);return cloaked!=0;}
 void RestoreShellPanels(){foreach(var (window,rect) in shellPanelOriginalRects)Native.SetWindowPos(window,IntPtr.Zero,rect.Left,rect.Top,0,0,0x0015);shellPanelOriginalRects.Clear();}
 async void OpenCalendar(object sender,RoutedEventArgs e)=>await ToggleNativeCalendarAsync();
 static async Task SendWindowsShortcutAsync(byte? key=null) {
  Native.keybd_event(0x5B,0x5B,1,UIntPtr.Zero);
  await Task.Delay(60);
  if(key is byte value){var scan=(byte)Native.MapVirtualKey(value,0);Native.keybd_event(value,scan,0,UIntPtr.Zero);await Task.Delay(40);Native.keybd_event(value,scan,2,UIntPtr.Zero);}
  Native.keybd_event(0x5B,0x5B,3,UIntPtr.Zero);
 }
 async Task<bool> ToggleNativeCalendarAsync() {
  if(calendarBusy||nativeTrayAccessInProgress)return false;calendarBusy=true;
  try {
   AnimateDock(false);revealUntil=DateTime.UtcNow.AddSeconds(3);PrepareShellPanels();
   var before=FindShellPanels(true).ToHashSet();
   if(calendarHandle!=IntPtr.Zero&&before.Contains(calendarHandle)) {
    Native.SetForegroundWindow(calendarHandle);
    if(Native.GetForegroundWindow()==calendarHandle){Native.keybd_event(0x1B,0,0,UIntPtr.Zero);Native.keybd_event(0x1B,0,2,UIntPtr.Zero);}
    else await SendWindowsShortcutAsync(0x4E);
    await Task.Delay(200);NativeTray.ReleaseCalendarAccess();calendarHandle=IntPtr.Zero;return true;
   }
   var taskbar=Native.FindWindow("Shell_TrayWnd",null);
   if(replacing)Native.EnableWindow(taskbar,true);
   Native.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
   await Task.Delay(100);
   if(!await NativeTray.OpenCalendarAsync(taskbar))await SendWindowsShortcutAsync(0x4E);
   for(int attempt=0;attempt<25;attempt++) {
    await Task.Delay(80);
    var visible=FindShellPanels(true).ToList();
    var candidate=visible.FirstOrDefault(window=>!before.Contains(window));
    if(candidate==IntPtr.Zero){var foreground=Native.GetForegroundWindow();if(visible.Contains(foreground))candidate=foreground;}
    if(candidate==IntPtr.Zero)candidate=visible.FirstOrDefault(window=>{var cls=new StringBuilder(256);Native.GetClassName(window,cls,256);return cls.ToString().StartsWith("XamlExplorerHostIslandWindow",StringComparison.Ordinal);});
    if(candidate==IntPtr.Zero)continue;
    calendarHandle=candidate;PositionShellPanel(candidate);Native.SetForegroundWindow(candidate);
    await Task.Delay(200);PositionShellPanel(candidate);return true;
   }
   NativeTray.ReleaseCalendarAccess();return false;
  }finally{if(replacing&&!NativeTray.TaskbarIsTransparent)Native.EnableWindow(Native.FindWindow("Shell_TrayWnd",null),false);calendarBusy=false;}
 }
 async Task<string> CheckNativeCalendarAsync() {
  if(!await ToggleNativeCalendarAsync())return "FAIL: calendrier Windows natif indisponible";
  await Task.Delay(250);PositionShellPanel(calendarHandle);
  Native.GetWindowRect(calendarHandle,out var rect);
  bool valid=calendarHandle!=IntPtr.Zero&&rect.Right-rect.Left>=120&&rect.Bottom-rect.Top>=120&&rect.Bottom<=(int)Math.Floor(PanelBottom*DpiScale());
  await ToggleNativeCalendarAsync();
  return valid?$"PASS: vrai calendrier Windows {rect.Right-rect.Left} × {rect.Bottom-rect.Top} px, au-dessus du dock":"FAIL: calendrier Windows recouvre le dock";
 }
}

