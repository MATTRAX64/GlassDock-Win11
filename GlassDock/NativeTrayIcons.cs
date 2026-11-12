using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassDock;

internal static partial class NativeTray
{
    internal sealed record TrayIcon(string Name, int Occurrence, BitmapSource? Image);
    private sealed record IconPeer(AutomationElement Element, string Name, int Occurrence, Rect Bounds);

    private static List<IconPeer> ReadIconPeers(IntPtr window)
    {
        var root = AutomationElement.FromHandle(window);
        var elements = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        var peers = new List<IconPeer>();
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (AutomationElement element in elements)
        {
            var properties = element.Current;
            if (properties.IsOffscreen || properties.BoundingRectangle.IsEmpty ||
                !(properties.AutomationId == "NotifyItemIcon" || properties.ClassName.Contains("SystemTray."))) continue;
            var name = string.IsNullOrWhiteSpace(properties.Name) ? "Icône de notification" : properties.Name;
            var occurrence = names.GetValueOrDefault(name);
            names[name] = occurrence + 1;
            peers.Add(new IconPeer(element, name, occurrence, properties.BoundingRectangle));
        }
        return peers;
    }

    internal static Task<List<TrayIcon>> ReadIconsAsync(IntPtr window) => Task.Run(() =>
    {
        var peers = ReadIconPeers(window);
        var capture = CaptureOverflow(window);
        return peers.Select(peer => new TrayIcon(peer.Name, peer.Occurrence,
            capture is null ? null : CropIcon(capture, peer.Bounds))).ToList();
    });

    private sealed record OverflowPixels(byte[] Pixels, Native.RECT Bounds, int Width, int Height);

    private static BitmapSource? CropIcon(OverflowPixels capture, Rect bounds)
    {
        var x = (int)Math.Round(bounds.X) - capture.Bounds.Left;
        var y = (int)Math.Round(bounds.Y) - capture.Bounds.Top;
        var width = (int)Math.Round(bounds.Width);
        var height = (int)Math.Round(bounds.Height);
        if (width < 8 || height < 8 || x < 0 || y < 0 ||
            x + width > capture.Width || y + height > capture.Height) return null;
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            Buffer.BlockCopy(capture.Pixels, ((y + row) * capture.Width + x) * 4, pixels, row * width * 4, width * 4);
        return SeparateIcon(pixels, width, height);
    }

    internal static async Task<bool> ActivateIconAsync(IntPtr window, TrayIcon item, bool contextMenu)
    {
        try
        {
            Point? point = null;
            for (var attempt = 0; attempt < 3 && point is null && IsOverflowVisible(); attempt++)
            {
                point = await Task.Run<Point?>(() =>
                {
                    var peer = ReadIconPeers(window).FirstOrDefault(candidate => candidate.Name == item.Name && candidate.Occurrence == item.Occurrence);
                    return peer is null ? null : new Point(peer.Bounds.Left + peer.Bounds.Width / 2, peer.Bounds.Top + peer.Bounds.Height / 2);
                }).WaitAsync(TimeSpan.FromSeconds(1));
                if (point is null && attempt < 2) await Task.Delay(35);
            }
            if (point is not { } position || !IsOverflowVisible() || OverflowWindow() != window) return false;
            var click = new Native.POINT { X = (int)Math.Round(position.X), Y = (int)Math.Round(position.Y) };
            if (GetAncestor(WindowFromPoint(click), 2) != window) return false;
            if (!Native.GetCursorPos(out var previous) || !SetCursorPos(click.X, click.Y)) return false;
            try
            {
                MouseEvent(contextMenu ? 0x0008u : 0x0002u, 0, 0, 0, UIntPtr.Zero);
                MouseEvent(contextMenu ? 0x0010u : 0x0004u, 0, 0, 0, UIntPtr.Zero);
                await Task.Delay(45);
            }
            finally
            {
                if (Native.GetCursorPos(out var current) && current.X == click.X && current.Y == click.Y)
                    SetCursorPos(previous.X, previous.Y);
            }
            return true;
        }
        catch (Exception error) when (error is ElementNotAvailableException or TimeoutException) { return false; }
    }

    private static OverflowPixels? CaptureOverflow(IntPtr window)
    {
        if (!Native.GetWindowRect(window, out var bounds)) return null;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width < 8 || height < 8 || width > 1600 || height > 1600) return null;
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        var memory = CreateCompatibleDC(screen);
        IntPtr bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            if (memory == IntPtr.Zero) return null;
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height,
                Planes = 1, BitCount = 32
            };
            bitmap = CreateDIBSection(memory, ref header, 0, out var pixels, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || pixels == IntPtr.Zero) return null;
            previous = SelectObject(memory, bitmap);
            if (!PrintWindow(window, memory, 2)) return null;
            var raw = new byte[width * height * 4];
            Marshal.Copy(pixels, raw, 0, raw.Length);
            return new OverflowPixels(raw, bounds, width, height);
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static BitmapSource? SeparateIcon(byte[] raw, int width, int height)
    {
        var side = Math.Min(width, height) * 3 / 4;
        var left = (width - side) / 2;
        var top = (height - side) / 2;
        var result = new byte[side * side * 4];
        var foregroundPixels = 0;
        for (var y = 0; y < side; y++)
        {
            var row = (top + y) * width * 4;
            for (var x = 0; x < side; x++)
            {
                var column = left + x;
                var source = row + column * 4;
                var destination = (y * side + x) * 4;
                var difference = 0d;
                for (var channel = 0; channel < 3; channel++)
                {
                    var start = raw[row + 4 + channel];
                    var end = raw[row + (width - 2) * 4 + channel];
                    var background = start + (end - start) * ((double)(column - 1) / (width - 3));
                    difference = Math.Max(difference, Math.Abs(raw[source + channel] - background));
                    result[destination + channel] = raw[source + channel];
                }
                result[destination + 3] = (byte)Math.Clamp((difference - 7) * 20, 0, 255);
                if (result[destination + 3] > 32) foregroundPixels++;
            }
        }
        if (foregroundPixels < 4 || foregroundPixels > side * side * .9) return null;
        var image = BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgra32, null, result, side * 4);
        image.Freeze();
        return image;
    }

    internal static bool HasInputBeenIdle(uint milliseconds)
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) && unchecked((uint)Environment.TickCount - info.Tick) >= milliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { internal uint Size, Tick; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, BitCount;
        internal uint Compression, ImageSize;
        internal int XPelsPerMeter, YPelsPerMeter;
        internal uint ColorsUsed, ColorsImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void MouseEvent(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
