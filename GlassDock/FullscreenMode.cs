using System.Runtime.InteropServices;
using System.Windows;

namespace GlassDock;

public partial class MainWindow {
 bool fullScreenSuppressed;
 DateTime windowsKeyRevealUntil;
 IntPtr windowsKeyHook;
 Native.KeyboardHookProc? windowsKeyCallback;

 void InitializeFullscreenMode() {
  windowsKeyCallback=(code,message,data)=> {
   if(code>=0&&(message==(IntPtr)0x100||message==(IntPtr)0x104)) {
    var key=Marshal.ReadInt32(data);
    if(key is 0x5B or 0x5C)Dispatcher.BeginInvoke(new Action(()=> {
     windowsKeyRevealUntil=DateTime.UtcNow.AddSeconds(5);
     fullScreenSuppressed=false;revealUntil=windowsKeyRevealUntil;AnimateDock(false);
     EnsureDockTopmost();
    }));
   }
   return Native.CallNextHookEx(windowsKeyHook,code,message,data);
  };
  windowsKeyHook=Native.SetWindowsHookEx(13,windowsKeyCallback,Native.GetModuleHandle(null),0);
  Closed+=(_,_)=>{if(windowsKeyHook!=IntPtr.Zero)Native.UnhookWindowsHookEx(windowsKeyHook);};
 }

 static bool FitsFullscreen(Native.RECT window,Native.RECT screen)=>
  Math.Abs(window.Left-screen.Left)<=2&&Math.Abs(window.Top-screen.Top)<=2&&
  Math.Abs(window.Right-screen.Right)<=2&&Math.Abs(window.Bottom-screen.Bottom)<=2;

 bool ForegroundIsFullscreen() {
  var handle=Native.GetForegroundWindow();
  if(handle==IntPtr.Zero)return false;
  Native.GetWindowThreadProcessId(handle,out var pid);
  if(pid==(uint)Environment.ProcessId)return false;
  var cls=new System.Text.StringBuilder(256);Native.GetClassName(handle,cls,256);
  if(cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")return false;
  if(Native.MonitorFromWindow(handle,2)!=Native.MonitorFromWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle,2))return false;
  return Native.GetWindowRect(handle,out var rect)&&FitsFullscreen(rect,ScreenBounds());
 }
}

internal static partial class Native {
 internal delegate IntPtr KeyboardHookProc(int code,IntPtr message,IntPtr data);
 [DllImport("user32.dll")]internal static extern IntPtr SetWindowsHookEx(int type,KeyboardHookProc callback,IntPtr module,uint thread);
 [DllImport("user32.dll")]internal static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
 [DllImport("user32.dll")]internal static extern bool UnhookWindowsHookEx(IntPtr hook);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]internal static extern IntPtr GetModuleHandle(string? name);
}
