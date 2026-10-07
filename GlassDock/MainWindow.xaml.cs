using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace GlassDock;
public class DockItem { public string Name { get; set; } = ""; public string Path { get; set; } = ""; public string IconPath {get;set;}=""; public List<DockItem>? Children { get; set; } public int Slot {get;set;}=-1;public int GridSize {get;set;}=9;public int IconSize {get;set;}=32;}
public partial class MainWindow : Window
{
 readonly string savePath = System.IO.Path.Combine(StateStore.DirectoryForRun(),"dock.json");
 List<DockItem> items = new();
 Native.RECT originalArea; bool replacing; uint originalTaskbarState;bool closeAfterNativeTray;
 readonly MenuItem ReplaceToggle=new() {IsCheckable=true};
 void OpenTaskManager(object sender,RoutedEventArgs e)=>Process.Start(new ProcessStartInfo("taskmgr.exe") {UseShellExecute=true});
 public MainWindow() {
  InitializeComponent();
  StartButton.Focusable=false;
  DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(new Action(PositionDock));
  SourceInitialized+=(_,_)=>System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(StartButtonActivation);
  items=StateStore.Read<List<DockItem>>(savePath)??new();
  if(items.Count == 0) items = [new() {Name="Explorateur",Path="explorer.exe"},new() {Name="Bloc-notes",Path="notepad.exe"},new() {Name="Applications",Children=new()}];
  Loaded += (_,_) => { InitializeFeatures(); PositionDock(); VerifyStartupIcons(); UpdateClock(); };
  var clock = new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
  clock.Tick += (_,_) => { UpdateClock(); PollWindows(); }; clock.Start();
  Closed += (_,_) => { Restore(); clock.Stop(); settingsWindow?.Close(); folderWindow?.Close(); SystemEvents.DisplaySettingsChanged-=DisplayChanged;Application.Current.Shutdown(); };
  SystemEvents.DisplaySettingsChanged += DisplayChanged;
  Closing+=(_,e)=>{if(nativeTrayAccessInProgress){e.Cancel=true;closeAfterNativeTray=true;return;}SaveStateOnExit();};
 }
 void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(PositionDock));
 void UpdateClock() {if(appliedDark!=Dark)ApplyTheme();ClockText.Text=DateTime.Now.ToString("HH:mm"); DateText.Text=DateTime.Now.ToString("dd/MM/yyyy");UpdateSystemGroup(); }
 async void OpenStart(object sender,RoutedEventArgs e) {PrepareShellPanels();await SendWindowsShortcutAsync();}
 IntPtr StartButtonActivation(IntPtr window,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
  // Keep Start's focus until the Windows-key toggle runs. Otherwise activating
  // our dock dismisses Start before Click and the same key immediately reopens it.
  if(message==0x21&&IsLoaded&&Native.GetCursorPos(out var cursor)&&((StartButton.IsVisible&&ScreenContains(StartButton,cursor.X,cursor.Y))||ScreenContains(ClockButton,cursor.X,cursor.Y)||(folderWindow?.IsVisible==true&&ScreenContains(ItemsPanel,cursor.X,cursor.Y)))) {
   handled=true;return new IntPtr(3); // WM_MOUSEACTIVATE: MA_NOACTIVATE, deliver the click.
  }
  return IntPtr.Zero;
 }
 internal void PositionDockToolTip(FrameworkElement anchor,ToolTip tip) {
  tip.PlacementTarget=anchor;
  tip.Placement=System.Windows.Controls.Primitives.PlacementMode.Custom;
  tip.HorizontalOffset=0;tip.VerticalOffset=0;
  tip.CustomPopupPlacementCallback=(popup,target,offset)=> {
   var surfaceTop=GlassSurface.TranslatePoint(new Point(),anchor).Y;
   return new[]{new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point((target.Width-popup.Width)/2,surfaceTop-popup.Height-8),System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)};
  };
 }
 internal bool SuppressFolderToolTip(FrameworkElement anchor)=>
  anchor is Button {Tag:DockItem item}&&item.Children is not null&&ReferenceEquals(openedFolder,item)&&folderWindow?.IsVisible==true;
 internal void ShowSettingsFromShortcut() {OpenSettings(this,new RoutedEventArgs());settingsWindow?.Activate();}
 internal static double NormalizeDockScale(double value)=>double.IsFinite(value)?Math.Clamp(Math.Round(value/25,MidpointRounding.AwayFromZero)*25,25,200):100;
 double DockScaleFactor()=>NormalizeDockScale(preferences.DockScale)/100;
 void PositionDock() {
  BeginAnimation(TopProperty,null);
  var scale=DockScaleFactor();
  GlassSurface.LayoutTransform=new System.Windows.Media.ScaleTransform(scale,scale);
  GlassSurface.Margin=new Thickness(8*scale);
  Height=76*scale;
  SystemButtons.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
  var screen=ScreenBounds();var dpi=DpiScale();
  var screenWidth=(screen.Right-screen.Left)/dpi;
  var maximum=Math.Max(1,screenWidth-24);
  var startSpace=preferences.StartMode=="Masqué"?0:55;
  var minimum=Math.Min(maximum,Math.Max(480,SystemButtons.DesiredSize.Width+startSpace+104+58)*scale);
  var percent=double.IsFinite(preferences.DockWidth)?Math.Clamp(preferences.DockWidth,50,100):100;
  Width=Math.Clamp(maximum*percent/100*scale,minimum,maximum);
  Left=screen.Left/dpi+(screenWidth-Width)/2;
  Top=DockTop(tucked);ApplyIconAlignment();
 }
 void Save() { PersistDock();Render(); }
 void Render() {
  var positions=CapturePositions();
  var previousIcons=CaptureRemovalIcons();
  ItemsPanel.Children.Clear();
  foreach(var item in items) {
   var button = new Button {Tag=item,Content=AppIconContent(IconFor(item)), ToolTip=item.Name,Width=46,Height=46,Padding=new Thickness(3)};
   WireDrag(button,item);
   button.Click += (_,_) => {if(item.Children is null) Launch(item); else OpenFolder(item,button);};
   button.ContextMenu=BuildAppMenu(item);
  ItemsPanel.Children.Add(button);
  }
  RenderRunning();
  UpdateAppIndicators();
  ApplyIconAlignment();AnimatePositions(positions);AnimateRemovedIcons(previousIcons);
 }
 object IconFor(DockItem item) {
  if(item.Children is not null) return FolderIcon(item);
  if(File.Exists(item.IconPath))try {var image=new System.Windows.Media.Imaging.BitmapImage();image.BeginInit();image.UriSource=new Uri(item.IconPath);image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();return new Image {Source=image,Width=28,Height=28};}catch{}
  try {
   var path=ShortcutIconPath(item.Path);
   if(!File.Exists(path)) {var systemPath=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),path);if(File.Exists(systemPath))path=systemPath;else path=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),path);}
   var info=new Native.SHFILEINFO();Native.SHGetFileInfo(path,0,ref info,(uint)Marshal.SizeOf<Native.SHFILEINFO>(),0x100);
   if(info.hIcon!=IntPtr.Zero) {try {var source=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(info.hIcon,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(32,32));source.Freeze();return new Image {Source=source,Width=28,Height=28};} finally {Native.DestroyIcon(info.hIcon);}}
  } catch { }
  return ReadCachedIcon(item)??new TextBlock {Text="▣",FontSize=26};
 }
 void OpenFolder(DockItem folder, Button anchor) {
  OpenFolderGrid(folder,anchor);
 }
 void Launch(DockItem item) {try {if(ActivateExisting(item))return;Process.Start(new ProcessStartInfo(item.Path){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message,"Impossible d’ouvrir l’application");} }
 void PickApp(List<DockItem> destination) {var dialog=new OpenFileDialog {Title="Choisir une application ou un raccourci",Filter="Applications et raccourcis|*.exe;*.lnk;*.url|Tous les fichiers|*.*",Multiselect=true,InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)};if(dialog.ShowDialog(this)==true){foreach(var path in dialog.FileNames)destination.Add(new DockItem {Name=System.IO.Path.GetFileNameWithoutExtension(path),Path=path});Save();} }
 void AddApp(object sender,RoutedEventArgs e)=>PickApp(items);
 void AddFolder(object sender,RoutedEventArgs e) {var name=AskName("Nouveau dossier");if(name is not null){items.Add(new DockItem {Name=name,Children=new()});Save();} }
 string? AskName(string initial) {
  var dialog=new Window {Title="Nom de l’élément",Width=370,SizeToContent=SizeToContent.Height,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Topmost=true};
  var stack=new StackPanel {Margin=new Thickness(15)};var input=new TextBox {Text=initial,Margin=new Thickness(0,0,0,12)};var ok=new Button {Content="Enregistrer",IsDefault=true};ok.Click+=(_,_)=>{if(!string.IsNullOrWhiteSpace(input.Text))dialog.DialogResult=true;};stack.Children.Add(input);stack.Children.Add(ok);dialog.Content=stack;input.SelectAll();input.Focus();ApplyWindowTheme(dialog);ApplyGlassFrame(dialog);return dialog.ShowDialog()==true?input.Text.Trim():null;
 }
 System.Threading.Tasks.Task taskbarToggleTask=System.Threading.Tasks.Task.CompletedTask;
 void Toggle(object sender,RoutedEventArgs e)=>taskbarToggleTask=ToggleAsync(sender,e);
 async System.Threading.Tasks.Task ToggleAsync(object sender,RoutedEventArgs e) {
  if(nativeTrayAccessInProgress) {var requested=ReplaceToggle.IsChecked;pendingTaskbarUpdate=()=>{ReplaceToggle.IsChecked=requested;Toggle(sender,e);};return;}
  if(ReplaceToggle.IsChecked && !replacing) {
   Native.SystemParametersInfo(48,0,ref originalArea,0);
   var appbar=new Native.APPBARDATA {size=(uint)Marshal.SizeOf<Native.APPBARDATA>()};originalTaskbarState=(uint)Native.SHAppBarMessage(4,ref appbar).ToUInt64();
   // Prepare Explorer's UIA peers once, before switching to the replacement.
   // Keeping these peers avoids toggling AppBar state when opening a flyout.
   appbar.parameter=(IntPtr)(originalTaskbarState&~1u);Native.SHAppBarMessage(10,ref appbar);
   Native.EnableWindow(Native.FindWindow("Shell_TrayWnd",null),true);
   Native.ShowWindow(Native.FindWindow("Shell_TrayWnd",null),4);
   await System.Threading.Tasks.Task.Delay(180);
   NativeTray.CacheChevron(Native.FindWindow("Shell_TrayWnd",null));
   for(int attempt=0;attempt<10&&!NativeTray.HasCachedChevron;attempt++) {
    await System.Threading.Tasks.Task.Delay(120);
    NativeTray.CacheChevron(Native.FindWindow("Shell_TrayWnd",null));
   }
   appbar.parameter=(IntPtr)(originalTaskbarState|1);Native.SHAppBarMessage(10,ref appbar);
   replacing=true; SuppressTaskbar();
   // Explorer recalculates its AppBar asynchronously after being hidden.
   await System.Threading.Tasks.Task.Delay(180);
   if(!replacing)return;
   var area=ScreenBounds();
   if(!Native.SystemParametersInfo(47,0,ref area,2)) {Restore();ReplaceToggle.IsChecked=false;MessageBox.Show(this,"Windows n’a pas accepté le changement de zone de travail.","GlassDock");}
   RefreshMaximized();
  } else if(!ReplaceToggle.IsChecked) Restore();
  PositionDock();
  preferences.ReplaceWindows=ReplaceToggle.IsChecked;SavePreferences();
  if(replacing) {preferences.ShowDock=true;Show();SavePreferences();}
 }
 double DpiScale()=>System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
 void SetTaskbars(bool visible) {Native.EnumWindows((handle,_)=>{var name=new System.Text.StringBuilder(256);Native.GetClassName(handle,name,256);if(name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")Native.ShowWindow(handle,visible?5:0);return true;},IntPtr.Zero);}
 void RefreshMaximized() {
  Native.EnumWindows((handle,_)=>{if(Native.IsZoomed(handle) && Native.IsWindowVisible(handle) && Native.MonitorFromWindow(handle,2)==Native.MonitorFromWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle,2)) {Native.ShowWindowAsync(handle,9);Native.ShowWindowAsync(handle,3);}return true;},IntPtr.Zero);
 }
 Native.MONITORINFO CurrentMonitorInfo() {var info=new Native.MONITORINFO {size=(uint)Marshal.SizeOf<Native.MONITORINFO>()};Native.GetMonitorInfo(Native.MonitorFromWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle,2),ref info);return info;}
 Native.RECT ScreenBounds()=>CurrentMonitorInfo().monitor;
 void Restore() {if(nativeTrayAccessInProgress){pendingTaskbarUpdate=Restore;return;}if(!replacing)return;NativeTray.ReleaseCalendarAccess();NativeTray.ReleaseQuickSettingsAccess();replacing=false;var appbar=new Native.APPBARDATA {size=(uint)Marshal.SizeOf<Native.APPBARDATA>(),parameter=(IntPtr)originalTaskbarState};Native.SHAppBarMessage(10,ref appbar);RestoreTaskbar();SetTaskbars(true);Native.SystemParametersInfo(47,0,ref originalArea,2);RefreshMaximized();}
 void CloseDock(object sender,RoutedEventArgs e) => Close();
}
internal static partial class Native {
 [StructLayout(LayoutKind.Sequential)] public struct POINT {public int X,Y;}
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle,out RECT rect);
 [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr handle,uint attribute,out int value,int size);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr handle);
 [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA {public uint size;public IntPtr handle;public uint message;public uint edge;public RECT rect;public IntPtr parameter;}
 [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO {public uint size;public RECT monitor;public RECT work;public uint flags;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFO info);
 [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint message,ref APPBARDATA data);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle,out uint processId);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr handle,System.Text.StringBuilder text,int max);
 [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr handle,uint command);
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr handle,int index);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
 [DllImport("user32.dll")] public static extern IntPtr GetClassLongPtr(IntPtr handle,int index);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct SHFILEINFO {public IntPtr hIcon;public int iIcon;public uint attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string displayName;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string typeName;}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SHGetFileInfo(string path,uint attributes,ref SHFILEINFO info,uint size,uint flags);
 [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
 [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr handle);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr handle);
 [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr handle,int cmd);
 [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr handle,uint flags);
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int Left,Top,Right,Bottom;}
 public delegate bool EnumProc(IntPtr handle,IntPtr param);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback,IntPtr param);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr handle,System.Text.StringBuilder name,int max);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle,int cmd);
 [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action,uint param,ref RECT value,uint flags);
}









