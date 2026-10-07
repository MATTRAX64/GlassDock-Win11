using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace GlassDock;

public partial class MainWindow {
 IntPtr positionedSystemFlyout;
 Native.RECT originalSystemFlyoutRect;
 bool originalSystemFlyoutTopmost;
 bool systemFlyoutAccessInProgress;
 DispatcherTimer? systemFlyoutTimer;
 bool IsSystemFlyoutOpen=>SystemFlyoutNative.QuickSettingsWindow()!=IntPtr.Zero;

 // Open the real Windows control centre and keep its surface above the dock.
 async Task<bool> ToggleWindowsQuickSettingsAsync() {
  if(systemFlyoutAccessInProgress||nativeTrayAccessInProgress)return false;
  systemFlyoutAccessInProgress=true;
  CloseWindowPreview();AnimateDock(false);revealUntil=DateTime.UtcNow.AddSeconds(2);
  var taskbar=Native.FindWindow("Shell_TrayWnd",null);
  if(replacing)Native.EnableWindow(taskbar,true);
  try {
   var wasOpen=SystemFlyoutNative.QuickSettingsWindow()!=IntPtr.Zero;
   var before=FindShellPanels(true).ToHashSet();
   if(!wasOpen)RestoreSystemFlyoutPlacement();
   Native.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
   await Task.Delay(100);
   await SendWindowsShortcutAsync(0x41);
   for(int attempt=0;attempt<20;attempt++) {
    await Task.Delay(70);
    var window=SystemFlyoutNative.QuickSettingsWindow();
    if(window==IntPtr.Zero&&!wasOpen){
     var candidate=FindShellPanels(true).FirstOrDefault(h=>h!=calendarHandle&&!before.Contains(h)&&SystemFlyoutNative.IsLegacyQuickSettingsSurface(h));
     if(candidate!=IntPtr.Zero){SystemFlyoutNative.LegacyWindow=candidate;window=candidate;}
    }
    if(wasOpen) {
     if(window==IntPtr.Zero){RestoreSystemFlyoutPlacement();return true;}
     continue;
    }
    if(window==IntPtr.Zero)continue;
    RememberSystemFlyout(window);PositionSystemFlyout(window);
    Native.SetForegroundWindow(window);
    // Explorer can finish its own placement during the opening animation.
    await Task.Delay(160);PositionSystemFlyout(window);
    EnsureSystemFlyoutTimer();
    return true;
   }
   NativeTray.ReleaseQuickSettingsAccess();return false;
  }finally{if(replacing)Native.EnableWindow(taskbar,false);systemFlyoutAccessInProgress=false;}
 }


 void RememberSystemFlyout(IntPtr window) {
  if(positionedSystemFlyout==window)return;
  RestoreSystemFlyoutPlacement();
  if(!Native.GetWindowRect(window,out originalSystemFlyoutRect))return;
  positionedSystemFlyout=window;
  if(SystemFlyoutNative.IsLegacyQuickSettingsSurface(window))SystemFlyoutNative.LegacyWindow=window;
  originalSystemFlyoutTopmost=(Native.GetWindowLong(window,-20)&8)!=0;
 }

 bool PositionSystemFlyout(IntPtr window) {
  if(!Native.IsWindowVisible(window)||!Native.GetWindowRect(window,out var rect))return false;
  var screen=ScreenBounds();var dpi=DpiScale();
  int gap=Math.Max(1,(int)Math.Ceiling(8*dpi));
  int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
  if(width<=0||height<=0)return false;
  // ControlCenterWindow is a tall XAML surface: its visible cards sit at its
  // bottom. Shift that surface, keeping its size, rather than clipping its cards.
  int right=Math.Min(screen.Right-gap,(int)Math.Round((Left+Width-8)*dpi));
  int x=Math.Clamp(right-width,screen.Left+gap,Math.Max(screen.Left+gap,screen.Right-gap-width));
  int bottom=Math.Min(screen.Bottom-gap,(int)Math.Floor(PanelBottom*dpi));
  int y=bottom-height;
  return Native.SetWindowPos(window,new IntPtr(-1),x,y,0,0,0x0011); // TOPMOST, no size, no activation
 }

 void EnsureSystemFlyoutTimer() {
  if(systemFlyoutTimer is null) {
   systemFlyoutTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(240)};
   systemFlyoutTimer.Tick+=(_,_)=> {
    if(!IsLoaded||SystemFlyoutNative.QuickSettingsWindow()!=positionedSystemFlyout) {RestoreSystemFlyoutPlacement();return;}
    PositionSystemFlyout(positionedSystemFlyout);
    revealUntil=DateTime.UtcNow.AddMilliseconds(650);
   };
  }
  systemFlyoutTimer.Start();
 }

 void RestoreSystemFlyoutPlacement() {
  systemFlyoutTimer?.Stop();
  if(positionedSystemFlyout==IntPtr.Zero)return;
  if(SystemFlyoutNative.IsWindow(positionedSystemFlyout)) {
   Native.SetWindowPos(positionedSystemFlyout,originalSystemFlyoutTopmost?new IntPtr(-1):new IntPtr(-2),originalSystemFlyoutRect.Left,originalSystemFlyoutRect.Top,0,0,0x0011);
  }
  positionedSystemFlyout=IntPtr.Zero;
  SystemFlyoutNative.LegacyWindow=IntPtr.Zero;
  NativeTray.ReleaseQuickSettingsAccess();
 }

 void SystemFlyoutsClosing(){RestoreSystemFlyoutPlacement();NativeTray.ReleaseQuickSettingsAccess();}

 // Invoked only by the integration test, after all agents release the desktop.
 // It moves no unrelated windows and closes only the exact control centre opened here.
 async Task<string> CheckSystemFlyoutPlacementAsync() {
  var alreadyOpen=SystemFlyoutNative.QuickSettingsWindow()!=IntPtr.Zero;
  bool openedHere=false;
  try {
   if(alreadyOpen) {
    var existing=SystemFlyoutNative.QuickSettingsWindow();RememberSystemFlyout(existing);PositionSystemFlyout(existing);
   } else {
    openedHere=true;
    if(!await ToggleWindowsQuickSettingsAsync())return "FAIL: les réglages rapides Windows ne se sont pas ouverts";
   }
   await Task.Delay(240);
   var window=SystemFlyoutNative.QuickSettingsWindow();
   if(window==IntPtr.Zero||!Native.GetWindowRect(window,out var rect))return "FAIL: fenêtre native de réglages rapides indisponible";
   var screen=ScreenBounds();var dockTop=(int)Math.Floor((DockTop(false)+8)*DpiScale());
   var topmost=(Native.GetWindowLong(window,-20)&8)!=0;
   return dockTop-rect.Bottom>=8&&rect.Left>=screen.Left&&rect.Right<=screen.Right&&topmost
    ?$"PASS: réglages rapides Windows au-dessus du dock ({dockTop-rect.Bottom} px), premier plan natif"
    :$"FAIL: réglages rapides placés {rect.Left},{rect.Top},{rect.Right},{rect.Bottom}; dock={dockTop}; topmost={topmost}";
  }finally{
   if(openedHere&&positionedSystemFlyout!=IntPtr.Zero&&Native.GetForegroundWindow()==positionedSystemFlyout) {
    Native.keybd_event(0x1B,0,0,UIntPtr.Zero);Native.keybd_event(0x1B,0,2,UIntPtr.Zero);
    await Task.Delay(120);
   }
   RestoreSystemFlyoutPlacement();
  }
 }
}

internal static class SystemFlyoutNative {
 internal static IntPtr LegacyWindow;
 internal static bool IsLegacyQuickSettingsSurface(IntPtr window){
  var cls=new StringBuilder(256);Native.GetClassName(window,cls,256);
  if(cls.ToString()!="Windows.UI.Core.CoreWindow")return false;
  Native.GetWindowThreadProcessId(window,out var pid);
  try{var name=System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;return name.Equals("ShellExperienceHost",StringComparison.OrdinalIgnoreCase)||name.Equals("ShellHost",StringComparison.OrdinalIgnoreCase);}catch{return false;}
 }
 internal static IntPtr QuickSettingsWindow() {
  if(LegacyWindow!=IntPtr.Zero&&Native.IsWindowVisible(LegacyWindow)){
   Native.DwmGetWindowAttribute(LegacyWindow,14,out var hidden,4);
   if(hidden==0)return LegacyWindow;
  }
  var found=IntPtr.Zero;
  var candidate=IntPtr.Zero;
  while((candidate=Native.FindWindowEx(IntPtr.Zero,candidate,"ControlCenterWindow",null))!=IntPtr.Zero){
   if(!Native.IsWindowVisible(candidate))continue;
   Native.DwmGetWindowAttribute(candidate,14,out var hidden,4);
   if(hidden==0&&Native.GetWindowRect(candidate,out var bounds)&&bounds.Right-bounds.Left>120&&bounds.Bottom-bounds.Top>120)return candidate;
  }
  Native.EnumWindows((window,_)=> {
   if(!Native.IsWindowVisible(window))return true;
   var cls=new StringBuilder(256);Native.GetClassName(window,cls,cls.Capacity);
   if(cls.ToString()!="ControlCenterWindow")return true;
   Native.DwmGetWindowAttribute(window,14,out var cloaked,sizeof(int));
   if(cloaked!=0)return true;
   if(Native.GetWindowRect(window,out var rect)&&rect.Right>rect.Left&&rect.Bottom>rect.Top) {found=window;return false;}
   return true;
  },IntPtr.Zero);
  return found;
 }
 [DllImport("user32.dll")]internal static extern bool IsWindow(IntPtr hwnd);
}
