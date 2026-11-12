using System.Runtime.InteropServices;
using System.Windows;

namespace GlassDock;

public partial class MainWindow {
 bool fullScreenSuppressed;
 DateTime windowsKeyRevealUntil;
 bool windowsKeyWasDown;
 void InitializeFullscreenMode() { }
 void PollWindowsKey() {
  bool down=(Native.GetAsyncKeyState(0x5B)&0x8000)!=0||(Native.GetAsyncKeyState(0x5C)&0x8000)!=0;
  if(down&&!windowsKeyWasDown) {
   windowsKeyRevealUntil=DateTime.UtcNow.AddSeconds(5);
   fullScreenSuppressed=false;revealUntil=windowsKeyRevealUntil;AnimateDock(false);EnsureDockTopmost();
  }
  windowsKeyWasDown=down;
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
 [DllImport("user32.dll")]internal static extern short GetAsyncKeyState(int key);
}
