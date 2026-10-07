using System.Runtime.InteropServices;
namespace GlassDock;
internal static partial class Native {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string? className,string? title);
 [DllImport("user32.dll",EntryPoint="MapVirtualKeyW")]public static extern uint MapVirtualKey(uint key,uint type);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam);
 public delegate void WinEventProc(IntPtr hook,uint eventId,IntPtr handle,int objectId,int childId,uint thread,uint time);
 [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEventProc callback,uint process,uint thread,uint flags);
 [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
 [DllImport("user32.dll")] public static extern bool EnableWindow(IntPtr handle,bool enabled);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr handle,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string className,string? name);
 [StructLayout(LayoutKind.Sequential)]public struct POWER {public byte ACLineStatus,BatteryFlag,BatteryLifePercent,SystemStatusFlag;public uint BatteryLifeTime,BatteryFullLifeTime;}
 [DllImport("kernel32.dll")]public static extern bool GetSystemPowerStatus(out POWER status);
}
