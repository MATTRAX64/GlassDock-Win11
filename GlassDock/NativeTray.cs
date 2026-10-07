using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace GlassDock;

/// <summary>Opens Explorer's actual notification overflow; it never substitutes app windows.</summary>
internal static class NativeTray
{
    internal sealed record Result(bool Success, IntPtr Window, int IconCount, string Detail);
    private const int ExtendedStyle = -20;
    private const int LayeredStyle = 0x00080000;
    private const uint AlphaAttribute = 2;
    private const uint NoSizeNoActivate = 0x0011;
    private static int invoking;
    private static int taskbarTransparent;
    private static IntPtr transparentTaskbar;
    private static InvokePattern? cachedChevron;
    internal static bool HasCachedChevron=>cachedChevron is not null;
    private static InvokePattern? cachedQuickSettings;
    private static InvokePattern? cachedClock;
    private static IDisposable? calendarScope;
    internal static void ReleaseCalendarAccess(){calendarScope?.Dispose();calendarScope=null;}
    internal static async Task<bool> OpenCalendarAsync(IntPtr taskbar) {
        if(cachedClock is null)return false;
        try{ReleaseQuickSettingsAccess();ReleaseCalendarAccess();calendarScope=new InvisibleTaskbarScope(taskbar);await Task.Run(()=>cachedClock.Invoke());await Task.Delay(200);return true;}
        catch{ReleaseCalendarAccess();cachedClock=null;return false;}
    }
    private static IDisposable? quickSettingsScope;
    internal static void ReleaseQuickSettingsAccess() {quickSettingsScope?.Dispose();quickSettingsScope=null;}
    internal static void CacheChevron(IntPtr taskbar)
    {
        try {
            var root=AutomationElement.FromHandle(taskbar);
            var buttons=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
            foreach(AutomationElement button in buttons) {
                if(!button.TryGetCurrentPattern(InvokePattern.Pattern,out var pattern))continue;
                if(IsChevron(button))cachedChevron=(InvokePattern)pattern;
                if(button.Current.Name.StartsWith("Horloge ")||button.Current.Name.StartsWith("Clock "))cachedClock=(InvokePattern)pattern;
                if(button.Current.ClassName=="SystemTray.OmniButtonCenter"||button.Current.Name.StartsWith("Réseau "))cachedQuickSettings=(InvokePattern)pattern;
            }
        } catch { /* Explorer may be restarting; opening can retry its live tree. */ }
    }
    internal static async Task<bool> OpenQuickSettingsAsync(IntPtr taskbar) {
        if(cachedQuickSettings is null)CacheChevron(taskbar);
        if(cachedQuickSettings is null)return false;
        try {ReleaseCalendarAccess();ReleaseQuickSettingsAccess();quickSettingsScope=new InvisibleTaskbarScope(taskbar);await Task.Run(()=>cachedQuickSettings.Invoke());await Task.Delay(300);return true;}
        catch(ElementNotAvailableException){cachedQuickSettings=null;return false;}
        catch(InvalidOperationException){return false;}
    }
    internal static bool TaskbarIsTransparent => Volatile.Read(ref taskbarTransparent) != 0;
    internal static bool IsTransparentTaskbar(IntPtr window) =>
        TaskbarIsTransparent && window == transparentTaskbar;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowStyle(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowStyle(IntPtr window, int index, int value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    internal static IntPtr OverflowWindow()
    {
        var modern = Native.FindWindow("TopLevelWindowForOverflowXamlIsland", null);
        return modern != IntPtr.Zero ? modern : Native.FindWindow("NotifyIconOverflowWindow", null);
    }

    internal static bool IsOverflowVisible()
    {
        var window = OverflowWindow();
        return window != IntPtr.Zero && Native.IsWindowVisible(window);
    }

    // A Shell_TrayWnd can relocate itself during ShowWindow. Setting alpha to zero BEFORE
    // showing it keeps every native taskbar pixel invisible even if Explorer changes its rect.
    // This scope restores the previous style, layered attributes, rect and enabled state.
    private sealed class InvisibleTaskbarScope : IDisposable
    {
        private readonly IntPtr window;
        private readonly int style;
        private readonly Native.RECT rect;
        private readonly bool enabled, visible, hadLayeredAttributes;
        private readonly uint key, flags;
        private readonly byte alpha;
        private bool changed, disposed;

        internal InvisibleTaskbarScope(IntPtr taskbar)
        {
            window = taskbar;
            Native.GetWindowRect(window, out rect);
            style = GetWindowStyle(window, ExtendedStyle);
            enabled = IsWindowEnabled(window);
            visible = Native.IsWindowVisible(window);
            hadLayeredAttributes = GetLayeredWindowAttributes(window, out key, out alpha, out flags);
            try
            {
                Native.ShowWindow(window, 0);
                changed = true;
                // Reset a per-pixel layered surface while hidden before applying alpha.
                // Explorer can leave WS_EX_LAYERED without readable alpha attributes.
                if((style & LayeredStyle)!=0&&!hadLayeredAttributes)
                    SetWindowStyle(window,ExtendedStyle,style & ~LayeredStyle);
                SetWindowStyle(window, ExtendedStyle, style | LayeredStyle);
                if ((GetWindowStyle(window, ExtendedStyle) & LayeredStyle) == 0 ||
                    !SetLayeredWindowAttributes(window, 0, 0, AlphaAttribute) ||
                    !GetLayeredWindowAttributes(window, out _, out var hiddenAlpha, out var hiddenFlags) ||
                    hiddenAlpha != 0 || (hiddenFlags & AlphaAttribute) == 0)
                    throw new InvalidOperationException("Windows a refusé l'ouverture invisible de la zone de notification.");
                transparentTaskbar = window;
                Interlocked.Exchange(ref taskbarTransparent, 1);
                // Move outside the complete virtual desktop as an additional precaution.
                var x = GetSystemMetrics(76) - Math.Max(1, rect.Right - rect.Left) - 64;
                var y = GetSystemMetrics(77) + GetSystemMetrics(79) + 64;
                Native.SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, 0x0015);
                Native.EnableWindow(window, true);
                Native.ShowWindow(window, 4); // SW_SHOWNOACTIVATE
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (disposed || !changed || !IsWindow(window)) return;
            disposed = true;
            Native.ShowWindow(window, 0); // Hide before restoring its normal alpha.
            Interlocked.Exchange(ref taskbarTransparent, 0);
            transparentTaskbar = IntPtr.Zero;
            if (hadLayeredAttributes) SetLayeredWindowAttributes(window, key, alpha, flags);
            else SetWindowStyle(window,ExtendedStyle,style & ~LayeredStyle);
            SetWindowStyle(window, ExtendedStyle, style);
            Native.SetWindowPos(window, IntPtr.Zero, rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0014);
            Native.EnableWindow(window, enabled);
            if (visible) Native.ShowWindow(window, 4);
        }
    }

    private static bool IsChevron(AutomationElement element)
    {
        var current = element.Current;
        if (current.ControlType != ControlType.Button) return false;
        if (current.AutomationId is "OverflowButton" or "SystemTrayOverflowButton") return true;
        var label = current.Name.Normalize(NormalizationForm.FormD);
        label = new string(label.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
            System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        return label.Contains("hidden icons") || label.Contains("icones cachees") ||
               label.Contains("icones masquees");
    }

    private static bool InvokeChevron(IntPtr taskbar, CancellationToken cancellation)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        var specificChevron = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new OrCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "OverflowButton"),
                new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayOverflowButton"),
                new PropertyCondition(AutomationElement.NameProperty, "Afficher les icônes cachées"),
                new PropertyCondition(AutomationElement.NameProperty, "Masquer les icônes cachées"),
                new PropertyCondition(AutomationElement.NameProperty, "Afficher les icônes masquées"),
                new PropertyCondition(AutomationElement.NameProperty, "Show hidden icons"),
                new PropertyCondition(AutomationElement.NameProperty, "Hide hidden icons")));
        do
        {
            cancellation.ThrowIfCancellationRequested();
            // Re-showing an offscreen, auto-hidden taskbar recreates its XAML peers
            // asynchronously. An empty first UIA tree is not proof that no chevron exists.
            var root = AutomationElement.FromHandle(taskbar);
            var exact = root.FindFirst(TreeScope.Descendants, specificChevron);
            if (exact is not null)
            {
                cancellation.ThrowIfCancellationRequested();
                if (exact.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
                {
                    cancellation.ThrowIfCancellationRequested();
                    ((InvokePattern)pattern).Invoke();
                    return true;
                }
            }
            else
            {
                // Other Windows languages/builds may expose a different label. This
                // slower fallback is restricted to the notification button peer class.
                var buttons = root.FindAll(TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                        new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton")));
                foreach (AutomationElement button in buttons)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!IsChevron(button)) continue;
                    if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        ((InvokePattern)pattern).Invoke();
                        return true;
                    }
                }
            }
            cancellation.WaitHandle.WaitOne(60);
        } while (deadline.ElapsedMilliseconds < 1800);
        return false;
    }

    private static int VisibleNotificationCount(IntPtr window)
    {
        if (window == IntPtr.Zero || !Native.IsWindowVisible(window)) return 0;
        var root = AutomationElement.FromHandle(window);
        var elements = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        var count = 0;
        foreach (AutomationElement element in elements)
        {
            var current = element.Current;
            if (current.ControlType == ControlType.Button &&
                !current.IsOffscreen && !current.BoundingRectangle.IsEmpty &&
                (current.AutomationId == "NotifyItemIcon" || current.ClassName.Contains("SystemTray."))) count++;
        }
        return count;
    }

    private static async Task<bool> InvokeInvisibleChevronAsync(IntPtr taskbar, CancellationTokenSource cancellation)
    {
        using var invisibleTaskbar = new InvisibleTaskbarScope(taskbar);
        var invoke = Task.Run(() => {
            if(cachedChevron is not null)try {cancellation.Token.ThrowIfCancellationRequested();cachedChevron.Invoke();return true;}catch(ElementNotAvailableException){cachedChevron=null;}catch(InvalidOperationException){cachedChevron=null;}
            return InvokeChevron(taskbar,cancellation.Token);
        });
        if (await Task.WhenAny(invoke, Task.Delay(2800)) != invoke)
        {
            cancellation.Cancel();
            _ = invoke.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            throw new TimeoutException("Explorer n'a pas répondu à temps. Réessaie dans un instant.");
        }
        return await invoke;
    }

    internal static async Task<Result> CloseAsync(IntPtr focusTarget = default)
    {
        if (Interlocked.CompareExchange(ref invoking, 1, 0) != 0)
            return new(false, IntPtr.Zero, 0, "La zone de notification est déjà en cours d'ouverture.");
        using var cancellation = new CancellationTokenSource();
        try
        {
            // Clicking the dock can dismiss Explorer's flyout through focus loss before
            // our Click handler runs. Let its closing animation finish before toggling.
            await Task.Delay(200);
            if (!IsOverflowVisible()) return new(true, IntPtr.Zero, 0, "Zone native déjà fermée.");
            var overflow = OverflowWindow();
            if (overflow != IntPtr.Zero && Native.GetForegroundWindow() == overflow)
            {
                // Escape follows the focused XAML input site, unlike posting WM_KEYDOWN
                // to the flyout's outer HWND. Never send it to another foreground app.
                Native.keybd_event(0x1B, 0, 0, UIntPtr.Zero);
                Native.keybd_event(0x1B, 0, 2, UIntPtr.Zero);
                for (var i = 0; i < 20 && IsOverflowVisible(); i++) await Task.Delay(20);
                if (!IsOverflowVisible()) return new(true, IntPtr.Zero, 0, "Zone native fermée.");
            }
            // Explorer dismisses its native flyout when focus returns to our dock, just
            // as when clicking anywhere else. Unlike hiding its HWND, this closes XAML's
            // actual model too and does not rebuild the taskbar while the flyout is open.
            if (focusTarget != IntPtr.Zero && IsWindow(focusTarget))
            {
                Native.SetForegroundWindow(focusTarget);
                for (var i = 0; i < 20 && IsOverflowVisible(); i++) await Task.Delay(20);
                if (!IsOverflowVisible()) return new(true, IntPtr.Zero, 0, "Zone native fermée.");
            }
            var taskbar = Native.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero || !await InvokeInvisibleChevronAsync(taskbar, cancellation))
                return new(false, IntPtr.Zero, 0, "Le bouton natif des icônes cachées est indisponible.");
            // Invoking the real chevron updates XAML's IsOpen too. Merely hiding its HWND
            // leaves the model open and makes the following click close instead of reopen.
            for (var i = 0; i < 20 && IsOverflowVisible(); i++) await Task.Delay(20);
            return !IsOverflowVisible()
                ? new(true, IntPtr.Zero, 0, "Zone native fermée.")
                : new(false, IntPtr.Zero, 0, "Explorer n'a pas encore fermé la zone de notification.");
        }
        catch (Exception error) { return new(false, IntPtr.Zero, 0, error.Message); }
        finally { Interlocked.Exchange(ref invoking, 0); }
    }

    internal static async Task<Result> OpenAsync(int anchorX, int anchorY, Native.RECT screen)
    {
        if (Interlocked.CompareExchange(ref invoking, 1, 0) != 0)
            return new(false, IntPtr.Zero, 0, "La zone de notification est déjà en cours d'ouverture.");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var taskbar = Native.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return new(false, IntPtr.Zero, 0, "La barre native d'Explorer est indisponible.");
            if (!IsOverflowVisible())
            {
                if (!await InvokeInvisibleChevronAsync(taskbar, cancellation))
                    return new(false, IntPtr.Zero, 0, "Windows ne propose pas de bouton d'icônes cachées.");
            }
            await Task.Delay(200); // Wait for the native flyout's opening animation/layout.

            // Explorer owns the icons, their context menus, updates and click actions.
            var overflow = OverflowWindow();
            if (overflow == IntPtr.Zero || !Native.IsWindowVisible(overflow))
                return new(false, IntPtr.Zero, 0, "Explorer n'a pas ouvert sa zone de notification.");
            Native.GetWindowRect(overflow, out var bounds);
            var width = Math.Max(1, bounds.Right - bounds.Left);
            var height = Math.Max(1, bounds.Bottom - bounds.Top);
            var left = Math.Clamp(anchorX - width / 2, screen.Left + 8, Math.Max(screen.Left + 8, screen.Right - width - 8));
            var top = Math.Clamp(anchorY - height - 10, screen.Top + 8, Math.Max(screen.Top + 8, screen.Bottom - height - 8));
            Native.SetWindowPos(overflow, (IntPtr)(-1), left, top, 0, 0, NoSizeNoActivate);
            Native.SetForegroundWindow(overflow);
            var read = Task.Run(() => VisibleNotificationCount(overflow));
            if (await Task.WhenAny(read, Task.Delay(900)) != read)
            {
                _ = read.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                return new(false, overflow, 0, "La zone native est ouverte ; Explorer n'a pas confirmé ses icônes à temps.");
            }
            var count = await read;
            if (count == 0)
            {
                return new(false, IntPtr.Zero, 0, "Aucune icône cachée n'est disponible dans la zone native.");
            }
            return new(true, overflow, count, $"Zone native ouverte · {count} icônes de notification · position {left},{top}.");
        }
        catch (Exception error)
        {
            return new(false, IntPtr.Zero, 0, $"Impossible d'ouvrir les icônes cachées : {error.Message}");
        }
        finally { Interlocked.Exchange(ref invoking, 0); }
    }
}
