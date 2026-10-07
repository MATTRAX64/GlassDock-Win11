using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace GlassDock;

public partial class MainWindow {
 void UpdateStatusIcons() {
  var audio=SystemStatus.ReadVolume();
  VolumeStatusIcon.Text=audio is not { } level?"\uE74F":level.Muted||level.Volume<=0?"\uE74F":level.Volume<.34?"\uE993":level.Volume<.67?"\uE994":"\uE995";
  VolumeStatusIcon.ToolTip=audio is { } value? $"{value.Volume:P0}":"Sortie audio indisponible";
  var network=SystemStatus.ReadNetwork();NetworkStatusIcon.Text=network.Glyph;NetworkStatusIcon.ToolTip=network.Description;
  NetworkDisconnectedSlash.Visibility=network.Description=="Wi-Fi non connecté"?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
 }
}

internal static class SystemStatus {
 internal static (float Volume,bool Muted)? ReadVolume() {
  object? enumerator=null;IMMDevice? device=null;object? endpoint=null;
  try {
   enumerator=new MMDeviceEnumerator();var api=(IMMDeviceEnumerator)enumerator;
   if(api.GetDefaultAudioEndpoint(0,1,out device)<0)return null;
   var iid=typeof(IAudioEndpointVolume).GUID;
   if(device.Activate(ref iid,23,IntPtr.Zero,out endpoint)<0)return null;
   var volume=(IAudioEndpointVolume)endpoint;
   if(volume.GetMasterVolumeLevelScalar(out var level)<0||volume.GetMute(out var muted)<0)return null;
   return (level,muted);
  }catch(COMException){return null;}
  finally {foreach(var obj in new[]{endpoint,device,enumerator})if(obj is not null&&Marshal.IsComObject(obj))Marshal.ReleaseComObject(obj);}
 }
 internal static (string Glyph,string Description) ReadNetwork() {
  try {
   var allAdapters=NetworkInterface.GetAllNetworkInterfaces();
   var adapters=allAdapters.Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)).ToList();
   if(adapters.Any(n=>n.NetworkInterfaceType==NetworkInterfaceType.Ethernet&&n.GetIPProperties().GatewayAddresses.Count>0))return ("\uE839","Ethernet connecté");
   var wifi=adapters.FirstOrDefault(n=>n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211);
   if(wifi is not null) {
    var connection=ReadWifi(wifi.Id);var quality=connection?.Quality;
    var name=connection?.Name??ReadNetworkName(wifi.Id)??"Wi-Fi";
    return (quality is null?"\uE701":quality<25?"\uE872":quality<50?"\uE873":quality<75?"\uE874":"\uE875",name);
   }
   return DisconnectedNetwork(allAdapters.Any(n=>n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211)||HasWirelessInterface());
  }catch(NetworkInformationException){return ("\uEB55","État réseau indisponible");}
 }
 internal static (string Glyph,string Description) DisconnectedNetwork(bool hasWifi) =>
  hasWifi?("\uE701","Wi-Fi non connecté"):("\uE839","Ethernet non connecté");
 internal static bool HasBattery(byte flags)=>flags!=255&&(flags&128)==0;
 static bool HasWirelessInterface() {
  IntPtr client=IntPtr.Zero,list=IntPtr.Zero;
  try {
   return WlanOpenHandle(2,IntPtr.Zero,out _,out client)==0&&WlanEnumInterfaces(client,IntPtr.Zero,out list)==0&&Marshal.ReadInt32(list)>0;
  }finally {if(list!=IntPtr.Zero)WlanFreeMemory(list);if(client!=IntPtr.Zero)WlanCloseHandle(client,IntPtr.Zero);}
 }
 [DllImport("wlanapi.dll")]static extern uint WlanEnumInterfaces(IntPtr handle,IntPtr reserved,out IntPtr list);
 [ComImport,Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"),InterfaceType(ComInterfaceType.InterfaceIsDual)]interface INetworkConnection {
  [return:MarshalAs(UnmanagedType.Interface)]object GetNetwork();
  bool IsConnectedToInternet { [return:MarshalAs(UnmanagedType.VariantBool)]get; }
  bool IsConnected { [return:MarshalAs(UnmanagedType.VariantBool)]get; }
  int GetConnectivity();
  Guid GetConnectionId();
  Guid GetAdapterId();
 }
 static string? ReadNetworkName(string adapterId) {
  object? manager=null,connections=null;
  try {
   manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"))!);
   connections=((dynamic)manager!).GetNetworkConnections();
   foreach(var entry in (System.Collections.IEnumerable)connections) {
    object? network=null;
    try {
     if(((INetworkConnection)entry).GetAdapterId()!=Guid.Parse(adapterId))continue;
     network=((INetworkConnection)entry).GetNetwork();return (string)((dynamic)network).GetName();
    }finally {if(network is not null&&Marshal.IsComObject(network))Marshal.ReleaseComObject(network);if(Marshal.IsComObject(entry))Marshal.ReleaseComObject(entry);}
   }
  }catch(COMException) { }
   catch(ArgumentException) { }
   catch(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
  finally {if(connections is not null&&Marshal.IsComObject(connections))Marshal.ReleaseComObject(connections);if(manager is not null&&Marshal.IsComObject(manager))Marshal.ReleaseComObject(manager);}
  return null;
 }
 static (int Quality,string Name)? ReadWifi(string adapterId) {
  IntPtr client=IntPtr.Zero,data=IntPtr.Zero;
  try {
   if(!Guid.TryParse(adapterId,out var id)||WlanOpenHandle(2,IntPtr.Zero,out _,out client)!=0)return null;
   if(WlanQueryInterface(client,ref id,7,IntPtr.Zero,out var size,out data,out _)!=0||size<576)return null;
   // WLAN_CONNECTION_ATTRIBUTES: association signal quality follows PHY type.
   var length=Math.Clamp(Marshal.ReadInt32(data,520),0,32);var bytes=new byte[length];Marshal.Copy(IntPtr.Add(data,524),bytes,0,length);
   return (Math.Clamp(Marshal.ReadInt32(data,572),0,100),System.Text.Encoding.UTF8.GetString(bytes));
  }finally {if(data!=IntPtr.Zero)WlanFreeMemory(data);if(client!=IntPtr.Zero)WlanCloseHandle(client,IntPtr.Zero);}
 }
 [DllImport("wlanapi.dll")]static extern uint WlanOpenHandle(uint version,IntPtr reserved,out uint negotiated,out IntPtr handle);
 [DllImport("wlanapi.dll")]static extern uint WlanQueryInterface(IntPtr handle,ref Guid id,int opcode,IntPtr reserved,out uint size,out IntPtr data,out int valueType);
 [DllImport("wlanapi.dll")]static extern void WlanFreeMemory(IntPtr data);
 [DllImport("wlanapi.dll")]static extern uint WlanCloseHandle(IntPtr handle,IntPtr reserved);
 [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]class MMDeviceEnumerator { }
 [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDeviceEnumerator {
  [PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out IntPtr devices);
  [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IMMDevice device);
 }
 [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDevice {
  [PreserveSig]int Activate(ref Guid iid,uint context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)]out object endpoint);
 }
 [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IAudioEndpointVolume {
  [PreserveSig]int RegisterControlChangeNotify(IntPtr notify);
  [PreserveSig]int UnregisterControlChangeNotify(IntPtr notify);
  [PreserveSig]int GetChannelCount(out uint count);
  [PreserveSig]int SetMasterVolumeLevel(float level,IntPtr context);
  [PreserveSig]int SetMasterVolumeLevelScalar(float level,IntPtr context);
  [PreserveSig]int GetMasterVolumeLevel(out float level);
  [PreserveSig]int GetMasterVolumeLevelScalar(out float level);
  [PreserveSig]int SetChannelVolumeLevel(uint channel,float level,IntPtr context);
  [PreserveSig]int SetChannelVolumeLevelScalar(uint channel,float level,IntPtr context);
  [PreserveSig]int GetChannelVolumeLevel(uint channel,out float level);
  [PreserveSig]int GetChannelVolumeLevelScalar(uint channel,out float level);
  [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,IntPtr context);
  [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);
 }
}
