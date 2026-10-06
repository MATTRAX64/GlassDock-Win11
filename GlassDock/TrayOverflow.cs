using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GlassDock;

public partial class MainWindow
{
    // SuppressTaskbarWindow must skip its normal hide operation during this short scope.
    // NativeTray makes the real taskbar transparent before it ever becomes visible.
    bool nativeTrayAccessInProgress;
    Action? pendingTaskbarUpdate;
    NativeTray.Result? lastNativeTrayResult;

    async void OpenHiddenTray(object sender, RoutedEventArgs e)
    {
        if (nativeTrayAccessInProgress) return;
        if (NativeTray.IsOverflowVisible()) { await TryCloseHiddenTrayAsync(); return; }
        var result = await TryOpenHiddenTrayAsync(sender as FrameworkElement);
        if (result.Success || !IsLoaded) return;
        // Explain genuine unavailability; never display opened apps under the hidden-icons chevron.
        var menu = new ContextMenu
        {
            PlacementTarget = sender as UIElement,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            MaxWidth = 340,
            Background = new System.Windows.Media.SolidColorBrush(Dark
                ? System.Windows.Media.Color.FromRgb(38,43,55)
                : System.Windows.Media.Color.FromRgb(244,247,252)),
            Foreground = Ink
        };
        menu.Items.Add(new MenuItem
        {
            Header = new TextBlock { Text = result.Detail, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 },
            IsEnabled = false
        });
        menu.IsOpen = true;
    }

    async Task<NativeTray.Result> TryOpenHiddenTrayAsync(FrameworkElement? anchor = null)
        => await OperateHiddenTrayAsync(true, anchor);

    async Task<NativeTray.Result> TryCloseHiddenTrayAsync()
        => await OperateHiddenTrayAsync(false, null);

    async Task<NativeTray.Result> OperateHiddenTrayAsync(bool open, FrameworkElement? anchor)
    {
        if (nativeTrayAccessInProgress)
            return new(false, IntPtr.Zero, 0, "La zone de notification est déjà en cours d'ouverture.");
        nativeTrayAccessInProgress = true;
        var resumeTimer = edgeTimer?.IsEnabled == true;
        edgeTimer?.Stop();
        AnimateDock(false);
        revealUntil = DateTime.UtcNow.AddSeconds(3);
        try
        {
            var point = anchor is not null
                ? anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0))
                : PointToScreen(new Point(Math.Max(0, Width - 300), 0));
            lastNativeTrayResult = open
                ? await NativeTray.OpenAsync((int)point.X, (int)point.Y, ScreenBounds())
                : await NativeTray.CloseAsync(new System.Windows.Interop.WindowInteropHelper(
                    IsVisible ? this : settingsWindow ?? this).Handle);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(savePath)!, "native-tray.log"),
                    $"{DateTime.Now:O} success={lastNativeTrayResult.Success} icons={lastNativeTrayResult.IconCount} {lastNativeTrayResult.Detail}{Environment.NewLine}");
            }
            catch { /* A read-only config directory must not break tray access. */ }
            return lastNativeTrayResult;
        }
        finally
        {
            nativeTrayAccessInProgress = false;
            if (replacing) SuppressTaskbar();
            if (resumeTimer && IsLoaded) edgeTimer?.Start();
            var pending = pendingTaskbarUpdate;
            pendingTaskbarUpdate = null;
            if (closeAfterNativeTray)
            {
                // Closing takes precedence over changes that could show the dock again.
                // Closed performs the final taskbar restoration after alpha has settled.
                closeAfterNativeTray = false;
                _ = Dispatcher.BeginInvoke(Close);
            }
            else if (pending is not null) _ = Dispatcher.BeginInvoke(pending);
        }
    }
}

