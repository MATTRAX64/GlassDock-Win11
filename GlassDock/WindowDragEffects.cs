using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GlassDock;

public partial class MainWindow
{
    private const uint MoveSizeStart = 0x000A;
    private const uint MoveSizeEnd = 0x000B;
    private const int MaximizeBox = 0x00010000;
    private const int MaximizeWindow = 3;
    private const int TopEdgeHeight = 8;
    private const double SnapGuideWidth = 412;
    private const double SnapGuideHeight = 64;
    private static readonly Rect[][] SnapLayouts =
    [
        [new(0, 0, .5, 1), new(.5, 0, .5, 1)],
        [new(0, 0, 2d / 3, 1), new(2d / 3, 0, 1d / 3, 1)],
        [new(0, 0, .5, 1), new(.5, 0, .5, .5), new(.5, .5, .5, .5)],
        [new(0, 0, .5, .5), new(.5, 0, .5, .5), new(0, .5, .5, .5), new(.5, .5, .5, .5)]
    ];
    private sealed record SnapZone(Border Highlight, ScaleTransform Scale, Rect HitArea, Rect Destination);

    private void InitializeWindowDragEffects()
    {
        Window? guide = null;
        List<SnapZone> zones = [];
        IntPtr draggedWindow = IntPtr.Zero;
        IntPtr currentMonitor = IntPtr.Zero;
        SnapZone? highlighted = null;
        var visibilityVersion = 0;
        bool leftInitialTopEdge = false;
        bool canMaximize = false;
        Native.RECT guideBounds = default;
        Native.RECT workArea = default;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(24) };

        void HideGuide()
        {
            if (currentMonitor == IntPtr.Zero || guide?.Content is not Border face) return;
            currentMonitor = IntPtr.Zero;
            if (highlighted is not null) AnimateSnapTile(highlighted, false);
            highlighted = null;
            var version = ++visibilityVersion;
            var fade = new DoubleAnimation(face.Opacity, 0, TimeSpan.FromMilliseconds(100));
            fade.Completed += (_, _) => { if (version == visibilityVersion) guide.Hide(); };
            face.BeginAnimation(OpacityProperty, fade);
        }

        void Stop()
        {
            timer.Stop();
            draggedWindow = IntPtr.Zero;
            HideGuide();
            AnimateDragSheen(0);
        }

        SnapZone? ZoneAt(Native.POINT cursor)
        {
            if (currentMonitor == IntPtr.Zero || guide?.IsVisible != true) return null;
            var x = (double)(cursor.X - guideBounds.Left) / (guideBounds.Right - guideBounds.Left);
            var y = (double)(cursor.Y - guideBounds.Top) / (guideBounds.Bottom - guideBounds.Top);
            return zones.FirstOrDefault(zone => zone.HitArea.Contains(x, y));
        }

        void Update()
        {
            if (!IsVisible || !SystemFlyoutNative.IsWindow(draggedWindow) || (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0)
            {
                Stop();
                return;
            }
            if (!Native.GetCursorPos(out var cursor)) return;
            var monitor = Native.MonitorFromPoint(cursor, 2);
            var info = new Native.MONITORINFO { size = (uint)Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfo(monitor, ref info)) return;
            var dpi = SnapMonitorScale(monitor);
            var atTopEdge = IsAtTopEdge(cursor, info.monitor, dpi);
            if (!atTopEdge) leftInitialTopEdge = true;
            var maximize = atTopEdge && leftInitialTopEdge && canMaximize;
            var width = Math.Min(info.monitor.Right - info.monitor.Left - 12, (int)Math.Round(SnapGuideWidth * dpi));
            var height = (int)Math.Round(SnapGuideHeight * dpi);
            var x = info.monitor.Left + (info.monitor.Right - info.monitor.Left - width) / 2;
            var y = info.monitor.Top + (int)Math.Round(TopEdgeHeight * dpi);
            if (maximize || width <= 0 || cursor.Y > y + height + 16 * dpi || cursor.X < x - 20 * dpi || cursor.X > x + width + 20 * dpi)
            {
                HideGuide();
                return;
            }
            if (monitor != currentMonitor)
            {
                guide ??= CreateSnapGuide(zones);
                var face = (Border)guide.Content;
                var handle = new WindowInteropHelper(guide).EnsureHandle();
                guideBounds = new Native.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
                currentMonitor = monitor;
                visibilityVersion++;
                face.Background = GlassFill();
                face.BorderBrush = GlassEdge();
                Native.SetWindowPos(handle, new IntPtr(-1), x, y, width, height, 0x0010);
                var from = guide.IsVisible ? face.Opacity : 0;
                AnimateSnapGuide(face, from);
                guide.Show();
                Native.SetWindowPos(handle, new IntPtr(-1), x, y, width, height, 0x0010);
            }
            workArea = info.work;
            var selected = ZoneAt(cursor);
            if (ReferenceEquals(selected, highlighted)) return;
            if (highlighted is not null) AnimateSnapTile(highlighted, false);
            if (selected is not null) AnimateSnapTile(selected, true);
            highlighted = selected;
        }

        timer.Tick += (_, _) => Update();
        Native.WinEventProc callback = (_, eventId, handle, objectId, childId, _, _) =>
        {
            if (objectId != 0 || childId != 0 || handle == IntPtr.Zero) return;
            if (eventId == MoveSizeEnd)
            {
                if (handle != draggedWindow) return;
                SnapZone? target = null;
                var maximize = false;
                if ((Native.GetAsyncKeyState(0x1B) & 0x8000) == 0 && Native.GetCursorPos(out var cursor))
                {
                    maximize = canMaximize && leftInitialTopEdge && IsAtTopEdge(cursor);
                    if (!maximize) target = ZoneAt(cursor);
                }
                var area = workArea;
                Stop();
                if (maximize)
                    Native.ShowWindowAsync(handle, MaximizeWindow);
                else if (target is not null)
                    ApplySnapZone(handle, area, target.Destination);
                return;
            }
            const int resizable = 0x40000;
            if (!IsVisible || !Native.IsWindowVisible(handle) || (Native.GetWindowLong(handle, -16) & resizable) == 0) return;
            if ((Native.GetAsyncKeyState(0x01) & 0x8000) == 0) return;
            // Move/size shares a WinEvent. Skip sizing borders when the app reports one.
            if (!Native.GetCursorPos(out var position) || IsSizingBorder(handle, position)) return;
            canMaximize = (Native.GetWindowLong(handle, -16) & MaximizeBox) != 0;
            leftInitialTopEdge = !IsAtTopEdge(position);
            draggedWindow = handle;
            AnimateDragSheen(1);
            Update();
            if (draggedWindow != IntPtr.Zero) timer.Start();
        };

        const uint skipOwnProcess = 2;
        var hook = Native.SetWinEventHook(MoveSizeStart, MoveSizeEnd, IntPtr.Zero, callback, 0, 0, skipOwnProcess);
        Closed += (_, _) =>
        {
            if (hook != IntPtr.Zero) Native.UnhookWinEvent(hook);
            GC.KeepAlive(callback);
            timer.Stop();
            visibilityVersion++;
            guide?.Close();
        };
    }

    private double SnapMonitorScale(IntPtr monitor) =>
        Native.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96d : DpiScale();

    private bool IsAtTopEdge(Native.POINT cursor)
    {
        var monitor = Native.MonitorFromPoint(cursor, 2);
        var info = new Native.MONITORINFO { size = (uint)Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return false;
        return IsAtTopEdge(cursor, info.monitor, SnapMonitorScale(monitor));
    }

    private static bool IsAtTopEdge(Native.POINT cursor, Native.RECT monitor, double scale) =>
        cursor.X >= monitor.Left && cursor.X < monitor.Right
        && cursor.Y >= monitor.Top && cursor.Y < monitor.Top + Math.Round(TopEdgeHeight * scale);

    private static void AnimateSnapGuide(Border face, double opacity)
    {
        var duration = TimeSpan.FromMilliseconds(175);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        face.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, 1, duration) { EasingFunction = ease });
        var shift = (TranslateTransform)face.RenderTransform;
        var start = opacity <= .01 ? -5 : shift.Y;
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(start, 0, duration) { EasingFunction = ease });
    }

    private static void AnimateSnapTile(SnapZone zone, bool selected)
    {
        var duration = TimeSpan.FromMilliseconds(selected ? 125 : 100);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var scale = selected ? 1.055 : 1;
        zone.Highlight.BeginAnimation(OpacityProperty, new DoubleAnimation(zone.Highlight.Opacity, selected ? 1 : 0, duration) { EasingFunction = ease });
        zone.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(zone.Scale.ScaleX, scale, duration) { EasingFunction = ease });
        zone.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(zone.Scale.ScaleY, scale, duration) { EasingFunction = ease });
    }

    private static bool IsSizingBorder(IntPtr handle, Native.POINT point)
    {
        var coordinates = new IntPtr(unchecked((int)(((uint)(ushort)point.Y << 16) | (ushort)point.X)));
        Native.SendMessageTimeout(handle, 0x0084, IntPtr.Zero, coordinates, 2, 25, out var result);
        return result.ToInt64() is >= 10 and <= 17;
    }

    private static void ApplySnapZone(IntPtr handle, Native.RECT area, Rect zone)
    {
        var width = area.Right - area.Left;
        var height = area.Bottom - area.Top;
        var x = area.Left + (int)Math.Round(zone.Left * width);
        var y = area.Top + (int)Math.Round(zone.Top * height);
        var right = area.Left + (int)Math.Round(zone.Right * width);
        var bottom = area.Top + (int)Math.Round(zone.Bottom * height);
        if (Native.IsZoomed(handle)) Native.ShowWindow(handle, 9);
        Native.SetWindowPos(handle, IntPtr.Zero, x, y, right - x, bottom - y, 0x4014);
    }

    private void AnimateDragSheen(double opacity)
    {
        var current = DragSheen.Opacity;
        DragSheen.Opacity = opacity;
        DragSheen.BeginAnimation(OpacityProperty, new DoubleAnimation(current, opacity, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    private Brush SnapTileBrush(bool active) => new LinearGradientBrush(
        active ? Color.FromRgb(126, 219, 255) : Color.FromArgb(110, 168, 203, 249),
        active ? Color.FromRgb(57, 109, 221) : Color.FromArgb(62, 108, 150, 225), 100);

    private Window CreateSnapGuide(List<SnapZone> zones)
    {
        const double width = SnapGuideWidth, height = SnapGuideHeight;
        var canvas = new Canvas { Width = width, Height = height };
        const double groupWidth = 91, groupHeight = 44;
        for (var index = 0; index < SnapLayouts.Length; index++)
        {
            foreach (var rectangle in SnapLayouts[index])
            {
                var left = 10 + index * 101 + rectangle.X * groupWidth;
                var top = 10 + rectangle.Y * groupHeight;
                var tileWidth = rectangle.Width * groupWidth;
                var tileHeight = rectangle.Height * groupHeight;
                var scale = new ScaleTransform(1, 1);
                var tile = new Grid
                {
                    Width = tileWidth - 4, Height = tileHeight - 4,
                    RenderTransform = scale, RenderTransformOrigin = new Point(.5, .5)
                };
                var frame = new Border
                {
                    CornerRadius = new CornerRadius(6), Background = SnapTileBrush(false),
                    BorderBrush = GlassEdge(), BorderThickness = new Thickness(1)
                };
                var highlight = new Border
                {
                    CornerRadius = new CornerRadius(6), Background = SnapTileBrush(true),
                    BorderBrush = GlassEdge(), BorderThickness = new Thickness(1), Opacity = 0
                };
                tile.Children.Add(frame);
                tile.Children.Add(highlight);
                Canvas.SetLeft(tile, left + 2);
                Canvas.SetTop(tile, top + 2);
                canvas.Children.Add(tile);
                zones.Add(new(highlight, scale, new Rect(left / width, top / height, tileWidth / width, tileHeight / height), rectangle));
            }
        }

        var guide = new Window
        {
            Width = width, Height = height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
            ShowInTaskbar = false, ShowActivated = false, Focusable = false, IsHitTestVisible = false,
            Content = new Border
            {
                Background = GlassFill(), BorderBrush = GlassEdge(), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16), RenderTransform = new TranslateTransform(), Child = new Viewbox { Stretch = Stretch.Fill, Child = canvas }
            }
        };
        guide.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(guide).Handle;
            const int extendedStyle = -20;
            const int transparentToolWindow = 0x080000A0;
            PreviewNative.SetWindowLongPtr(handle, extendedStyle, (IntPtr)(Native.GetWindowLong(handle, extendedStyle) | transparentToolWindow));
        };
        return guide;
    }
}

internal static partial class Native
{
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}

