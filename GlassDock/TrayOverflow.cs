using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;

namespace GlassDock;

public partial class MainWindow
{
    bool nativeTrayAccessInProgress;
    TaskCompletionSource? trayOperation;
    Action? pendingTaskbarUpdate;
    Popup? hiddenTrayPopup;
    bool hiddenTrayArrowOpen;
    bool closingTrayPopup;
    long suppressTrayClickUntil;
    IReadOnlyList<NativeTray.TrayIcon> traySnapshot = [];
    Popup? trayLoadingPopup;
    DateTimeOffset lastTrayRefresh;
    string TraySnapshotPath => Path.Combine(Path.GetDirectoryName(savePath)!, "tray-icons.json");
    private sealed record StoredTrayIcon(string Name, int Occurrence, byte[]? Png);

    void InitializeTrayCache()
    {
        if (Environment.GetCommandLineArgs().Any(arg => arg == "--self-test" || arg.StartsWith("--test-", StringComparison.Ordinal))) return;
        traySnapshot = ReadTraySnapshot();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (!IsVisible || !replacing || dragging || nativeTrayAccessInProgress
                || hiddenTrayPopup?.IsOpen == true || fullScreenSuppressed
                || IsSystemFlyoutOpen || IsShellPanelOpen || folderWindow?.IsVisible == true
                || settingsWindow?.IsVisible == true || NativeTray.IsOverflowVisible()
                || !NativeTray.HasInputBeenIdle(1200)
                || DateTimeOffset.UtcNow - lastTrayRefresh < TimeSpan.FromSeconds(35)) return;
            lastTrayRefresh = DateTimeOffset.UtcNow;
            await RefreshTrayIconsAsync(HiddenTrayButton, false);
        };
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    IReadOnlyList<NativeTray.TrayIcon> ReadTraySnapshot()
    {
        var stored = StateStore.Read<List<StoredTrayIcon>>(TraySnapshotPath);
        if (stored is null) return [];
        var result = new List<NativeTray.TrayIcon>();
        foreach (var icon in stored.Take(100))
        {
            if (string.IsNullOrWhiteSpace(icon.Name)) continue;
            BitmapSource? image = null;
            if (icon.Png is { Length: > 0 and < 100000 })
            {
                try
                {
                    using var stream = new MemoryStream(icon.Png);
                    var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    frame.Freeze();
                    image = frame;
                }
                catch (Exception error) when (error is IOException or NotSupportedException or FormatException) { }
            }
            result.Add(new NativeTray.TrayIcon(icon.Name, icon.Occurrence, image));
        }
        return result;
    }

    void SaveTraySnapshot(IReadOnlyList<NativeTray.TrayIcon> icons)
    {
        try
        {
            var stored = new List<StoredTrayIcon>();
            foreach (var icon in icons.Take(100))
            {
                byte[]? png = null;
                if (icon.Image is not null)
                {
                    using var stream = new MemoryStream();
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(icon.Image));
                    encoder.Save(stream);
                    png = stream.ToArray();
                }
                stored.Add(new StoredTrayIcon(icon.Name, icon.Occurrence, png));
            }
            StateStore.Write(TraySnapshotPath, stored);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException) { }
    }

    void UpdateHiddenTrayArrow()
    {
        var open = hiddenTrayPopup?.IsOpen == true;
        if (!nativeTrayAccessInProgress && !NativeTray.IsOverflowVisible())
            NativeTray.ReleaseOverflowConcealment();
        if (open == hiddenTrayArrowOpen) return;
        hiddenTrayArrowOpen = open;
        var from = HiddenTrayRotation.Angle;
        var to = open ? 180d : 0d;
        HiddenTrayRotation.Angle = to;
        HiddenTrayRotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(145))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
    }

    async void OpenHiddenTray(object sender, RoutedEventArgs e)
    {
        if (suppressTrayClickUntil != 0)
        {
            var suppressed = Environment.TickCount64 <= suppressTrayClickUntil;
            suppressTrayClickUntil = 0;
            if (suppressed) return;
        }
        if (hiddenTrayPopup?.IsOpen == true)
        {
            CloseHiddenTrayPopup();
            if (!nativeTrayAccessInProgress && NativeTray.IsOverflowVisible()) await TryCloseHiddenTrayAsync();
            return;
        }
        var anchor = sender as FrameworkElement ?? HiddenTrayButton;
        if (traySnapshot.Count > 0)
        {
            ShowHiddenTrayPopup(anchor, traySnapshot);
            return;
        }

        ShowHiddenTrayPopup(anchor, [], "Chargement des icônes…");
        trayLoadingPopup = hiddenTrayPopup;
        if (nativeTrayAccessInProgress) return;
        var loading = trayLoadingPopup;
        var result = await RefreshTrayIconsAsync(anchor, false);
        if (!result.Success && IsLoaded && IsVisible && loading?.IsOpen == true && ReferenceEquals(loading, hiddenTrayPopup))
            ShowHiddenTrayPopup(anchor, [], result.Detail);
    }

    private static readonly ControlTemplate TrayButtonTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="Tile" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="7">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Tile" Property="Background" Value="{DynamicResource TrayTileHover}"/>
              <Setter TargetName="Tile" Property="BorderBrush" Value="{DynamicResource TrayTileEdge}"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Tile" Property="Background" Value="{DynamicResource TrayTileHover}"/>
              <Setter TargetName="Tile" Property="BorderBrush" Value="{DynamicResource TrayTileEdge}"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Tile" Property="Opacity" Value=".78"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    void ShowHiddenTrayPopup(FrameworkElement anchor, IReadOnlyList<NativeTray.TrayIcon> icons, string? message = null)
    {
        CloseHiddenTrayPopup();
        trayLoadingPopup = null;
        var dpi = VisualTreeHelper.GetDpi(anchor);
        var screen = CurrentMonitorInfo().work;
        var dockTop = GlassSurface.PointToScreen(new Point()).Y;
        var availableHeight = Math.Max(32, (dockTop - screen.Top) / dpi.DpiScaleY - 18);
        var availableWidth = Math.Max(32, (screen.Right - screen.Left) / dpi.DpiScaleX - 24);
        var columns = Math.Clamp((int)(availableWidth / 34), 1, 5);
        var grid = new UniformGrid { Columns = Math.Min(columns, Math.Max(1, icons.Count)) };
        grid.Resources["TrayTileHover"] = new SolidColorBrush(Dark
            ? Color.FromArgb(105, 141, 179, 228) : Color.FromArgb(175, 255, 255, 255));
        grid.Resources["TrayTileEdge"] = GlassEdge();
        foreach (var icon in icons)
        {
            FrameworkElement artwork = icon.Image is not null
                ? new Image { Source = icon.Image, Width = 29, Height = 29, Stretch = Stretch.Uniform }
                : new TextBlock { Text = icon.Name[..1].ToUpperInvariant(), Foreground = Ink, FontSize = 15 };
            var button = new Button
            {
                Width = 32, Height = 32, Margin = new Thickness(1), Padding = new Thickness(0),
                Content = artwork, Tag = icon, ToolTip = icon.Name, Background = Brushes.Transparent,
                Foreground = Ink, Cursor = Cursors.Hand, Template = TrayButtonTemplate
            };
            System.Windows.Automation.AutomationProperties.SetName(button, icon.Name);
            button.Click += async (_, _) => await InvokeHiddenTrayIconAsync(anchor, icon, false);
            button.PreviewMouseRightButtonUp += async (_, e) =>
            {
                e.Handled = true;
                await InvokeHiddenTrayIconAsync(anchor, icon, true);
            };
            grid.Children.Add(button);
        }
        FrameworkElement body = icons.Count == 0
            ? new TextBlock { Text = message ?? "Aucune icône masquée.", Foreground = Ink, TextWrapping = TextWrapping.Wrap, MaxWidth = 250, Margin = new Thickness(8) }
            : new ScrollViewer { Content = grid, MaxHeight = Math.Min(availableHeight, 204), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var rim = new Border
        {
            Margin = new Thickness(6), Padding = new Thickness(4), CornerRadius = new CornerRadius(15),
            BorderThickness = new Thickness(1), BorderBrush = GlassEdge(), Background = GlassFill(), Child = body,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 3, Opacity = .35 },
            RenderTransform = new TranslateTransform()
        };
        var popup = new Popup
        {
            AllowsTransparency = true, StaysOpen = nativeTrayAccessInProgress, PlacementTarget = anchor, Placement = PlacementMode.Custom,
            Child = rim,
            CustomPopupPlacementCallback = (size, target, _) =>
            {
                var origin = anchor.PointToScreen(new Point());
                var left = (screen.Left + 4 - origin.X) / dpi.DpiScaleX;
                var right = (screen.Right - 4 - origin.X) / dpi.DpiScaleX;
                var x = Math.Clamp((target.Width - size.Width) / 2, left, Math.Max(left, right - size.Width));
                var y = (GlassSurface.PointToScreen(new Point()).Y - origin.Y) / dpi.DpiScaleY - size.Height - 3;
                return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)];
            }
        };
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(hiddenTrayPopup, popup))
            {
                // WPF may dismiss the popup on mouse-down before the chevron's Click runs.
                if (!closingTrayPopup && (Native.GetAsyncKeyState(0x01) & 0x8000) != 0
                    && Native.GetCursorPos(out var pointer) && ScreenContains(HiddenTrayButton, pointer.X, pointer.Y))
                    SuppressDismissedChevronClick();
                hiddenTrayPopup = null;
                if (ReferenceEquals(trayLoadingPopup, popup)) trayLoadingPopup = null;
            }
            UpdateHiddenTrayArrow();
        };
        rim.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CloseHiddenTrayPopup(); e.Handled = true; } };
        hiddenTrayPopup = popup;
        popup.IsOpen = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() => PlaceHiddenTrayPopup(popup, rim, anchor)));
        rim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(145)));
        ((TranslateTransform)rim.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(145)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        UpdateHiddenTrayArrow();
    }

    void PlaceHiddenTrayPopup(Popup popup, FrameworkElement face, FrameworkElement anchor)
    {
        if (!popup.IsOpen || !anchor.IsLoaded || PresentationSource.FromVisual(face) is not HwndSource source
            || !Native.GetWindowRect(source.Handle, out var bounds)) return;
        var screen = CurrentMonitorInfo().work;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var center = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0)).X;
        var dockTop = GlassSurface.PointToScreen(new Point()).Y;
        var x = Math.Clamp((int)Math.Round(center - width / 2d), screen.Left + 4, Math.Max(screen.Left + 4, screen.Right - width - 4));
        var y = Math.Max(screen.Top + 4, (int)Math.Floor(dockTop) - height - 5);
        Native.SetWindowPos(source.Handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
    }

    void SuppressDismissedChevronClick() => suppressTrayClickUntil = Environment.TickCount64 + 650;

    void CloseHiddenTrayPopup()
    {
        closingTrayPopup = true;
        try { if (hiddenTrayPopup is { } popup) popup.IsOpen = false; }
        finally { closingTrayPopup = false; }
        hiddenTrayPopup = null;
        UpdateHiddenTrayArrow();
    }

    Point TrayAnchor(FrameworkElement anchor) => anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
    IntPtr TrayFocusTarget() => new WindowInteropHelper(IsVisible ? this : settingsWindow ?? this).Handle;

    Task<NativeTray.Result> TryOpenHiddenTrayAsync(FrameworkElement? anchor = null)
        => RefreshTrayIconsAsync(anchor ?? HiddenTrayButton, true);

    async Task<NativeTray.Result> RefreshTrayIconsAsync(FrameworkElement anchor, bool showPopup)
    {
        if (nativeTrayAccessInProgress) return new(false, IntPtr.Zero, 0, "Lecture des icônes en cours.");
        lastTrayRefresh = DateTimeOffset.UtcNow;
        var foreground = Native.GetForegroundWindow();
        BeginTrayOperation(out var resumeTimer, showPopup);
        try
        {
            var position = TrayAnchor(anchor);
            var native = await NativeTray.OpenAsync((int)position.X, (int)position.Y, ScreenBounds(), activate: showPopup, waitForArtwork: true);
            var icons = native.Success ? await NativeTray.ReadIconsAsync(native.Window).WaitAsync(TimeSpan.FromSeconds(2)) : [];
            if (NativeTray.IsOverflowVisible()) await NativeTray.CloseAsync(foreground);
            if (!native.Success || icons.Count == 0) return native with { Success = false };
            var captured = icons.Count(icon => icon.Image is not null);
            var cached = traySnapshot.ToDictionary(icon => (icon.Name, icon.Occurrence), icon => icon.Image);
            for (var index = 0; index < icons.Count; index++)
            {
                var icon = icons[index];
                if (icon.Image is null && cached.TryGetValue((icon.Name, icon.Occurrence), out var artwork))
                    icons[index] = icon with { Image = artwork };
            }
            traySnapshot = icons;
            var waiting = trayLoadingPopup?.IsOpen == true && ReferenceEquals(trayLoadingPopup, hiddenTrayPopup);
            if (IsVisible && !closeAfterNativeTray && pendingTaskbarUpdate is null && (showPopup || waiting))
                ShowHiddenTrayPopup(anchor, icons);
            SaveTraySnapshot(icons);
            WriteTrayLog($"Grille GlassDock · {icons.Count} icônes · {captured} images capturées");
            return native with { IconCount = icons.Count, Detail = "Grille GlassDock reconstruite." };
        }
        catch (Exception error)
        {
            if (NativeTray.IsOverflowVisible()) await NativeTray.CloseAsync(foreground);
            return new(false, IntPtr.Zero, 0, error.Message);
        }
        finally { EndTrayOperation(resumeTimer); }
    }

    async Task<NativeTray.Result> TryCloseHiddenTrayAsync()
    {
        CloseHiddenTrayPopup();
        return NativeTray.IsOverflowVisible() ? await NativeTray.CloseAsync(TrayFocusTarget())
            : new(true, IntPtr.Zero, 0, "Grille GlassDock fermée.");
    }

    async Task<bool> InvokeHiddenTrayIconAsync(FrameworkElement anchor, NativeTray.TrayIcon icon, bool contextMenu)
    {
        var popup = hiddenTrayPopup;
        if (trayOperation is { } current)
        {
            await current.Task;
            if (!ReferenceEquals(popup, hiddenTrayPopup) || popup?.IsOpen != true) return false;
        }
        if (nativeTrayAccessInProgress) return false;
        CloseHiddenTrayPopup();
        BeginTrayOperation(out var resumeTimer);
        try
        {
            var position = TrayAnchor(anchor);
            var native = await NativeTray.OpenAsync((int)position.X, (int)position.Y, ScreenBounds());
            var activated = native.Success && await NativeTray.ActivateIconAsync(native.Window, icon, contextMenu);
            if ((!activated || (!contextMenu && Native.GetForegroundWindow() == native.Window)) && NativeTray.IsOverflowVisible())
                await NativeTray.CloseAsync(TrayFocusTarget());
            if (!activated && IsLoaded && IsVisible && !contextMenu)
                ShowHiddenTrayPopup(anchor, [], "Cette icône a changé. Rouvre les icônes masquées.");
            WriteTrayLog($"Action notification · {(contextMenu ? "menu" : "clic")} · succès={activated}{(activated ? "" : $" · {native.Detail}")}");
            return activated;
        }
        catch (Exception error)
        {
            if (NativeTray.IsOverflowVisible()) await NativeTray.CloseAsync(TrayFocusTarget());
            if (IsLoaded && IsVisible && !contextMenu) ShowHiddenTrayPopup(anchor, [], error.Message);
            else WriteTrayLog($"Menu de notification indisponible · {error.Message}");
            return false;
        }
        finally
        {
            EndTrayOperation(resumeTimer);
        }
    }

    void BeginTrayOperation(out bool resumeTimer, bool reveal = true)
    {
        nativeTrayAccessInProgress = true;
        trayOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        resumeTimer = edgeTimer?.IsEnabled == true;
        edgeTimer?.Stop();
        if (reveal)
        {
            AnimateDock(false);
            revealUntil = DateTime.UtcNow.AddSeconds(3);
        }
    }

    void EndTrayOperation(bool resumeTimer)
    {
        nativeTrayAccessInProgress = false;
        var completed = trayOperation;
        trayOperation = null;
        if (hiddenTrayPopup is { IsOpen: true } popup) popup.StaysOpen = false;
        UpdateHiddenTrayArrow();
        if (replacing) SuppressTaskbar();
        if (resumeTimer && IsLoaded) edgeTimer?.Start();
        var pending = pendingTaskbarUpdate;
        pendingTaskbarUpdate = null;
        if (closeAfterNativeTray)
        {
            closeAfterNativeTray = false;
            _ = Dispatcher.BeginInvoke(Close);
        }
        else if (pending is not null) _ = Dispatcher.BeginInvoke(pending);
        completed?.TrySetResult();
    }

    void WriteTrayLog(string message)
    {
        try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(savePath)!, "native-tray.log"), $"{DateTime.Now:O} {message}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
