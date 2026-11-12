using System.Windows;
using System.Diagnostics;
using System.ComponentModel;
using System.Threading;
using System.Windows.Threading;
namespace GlassDock;
public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? cleanExit;
    private EventWaitHandle? settingsRequest;
    private RegisteredWaitHandle? settingsRegistration;
    private bool ownsInstance;
    private volatile bool exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (DockRecovery.IsWatcher(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await DockRecovery.WatchAsync(e.Args);
            Shutdown();
            return;
        }

        if (e.Args.Contains("--register-startup"))
        {
            try { GlassDock.MainWindow.SetAutoStart(true); }
            catch (Exception error)
            {
                Trace.TraceError("GlassDock startup registration: {0}", error.Message);
                Shutdown(1);
                return;
            }
            Shutdown();
            return;
        }

        var atLogon = e.Args.Contains("--startup");
        // Create/open the signal first: a second launch during initial startup can set
        // it before the first instance registers its listener without losing the request.
        settingsRequest = new EventWaitHandle(false, EventResetMode.AutoReset,
            "Local\\GlassDock.ShowSettings");
        instance = new Mutex(true, "Local\\GlassDock.Main", out var created);
        ownsInstance = created;
        if (!created)
        {
            if (!atLogon) settingsRequest.Set();
            Shutdown();
            return;
        }

        using (var process = Process.GetCurrentProcess())
        {
            try { process.PriorityClass = ProcessPriorityClass.High; }
            catch (Win32Exception ex) { Trace.TraceWarning("GlassDock priority: {0}", ex.Message); }
        }
        if (!e.Args.Any(arg => arg == "--self-test" || arg.StartsWith("--test-", StringComparison.Ordinal)))
            cleanExit = DockRecovery.Start(e.Args);

        // The Startup folder can launch GlassDock before Explorer creates its taskbar.
        if (atLogon)
        {
            for (var attempt = 0; attempt < 40 && Native.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero; attempt++)
                await Task.Delay(250);
        }

        settingsRegistration = ThreadPool.RegisterWaitForSingleObject(settingsRequest,
            (_, _) =>
            {
                if (exiting || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
                // WPF controls remain on their dispatcher. ApplicationIdle also lets
                // the main window load before an early wake request.
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    if (!exiting && MainWindow is GlassDock.MainWindow dock)
                        dock.ShowSettingsFromShortcut();
                }));
            }, null, Timeout.Infinite, executeOnlyOnce: false);
        base.OnStartup(e);
        EventManager.RegisterClassHandler(typeof(FrameworkElement),System.Windows.Controls.ToolTipService.ToolTipOpeningEvent,new System.Windows.Controls.ToolTipEventHandler((sender,tooltipEvent)=>
        {
            var anchor=(FrameworkElement)sender;
            if(anchor.ToolTip is null)return;
            var dock=Window.GetWindow(anchor) as GlassDock.MainWindow;
            if(dock?.SuppressFolderToolTip(anchor)==true){tooltipEvent.Handled=true;return;}
            var tip=anchor.ToolTip as System.Windows.Controls.ToolTip;
            if(tip is null){tip=new System.Windows.Controls.ToolTip{Content=anchor.ToolTip};anchor.ToolTip=tip;}
            if(dock is not null){dock.PositionDockToolTip(anchor,tip);return;}
            tip.PlacementTarget=anchor;
            tip.Placement=System.Windows.Controls.Primitives.PlacementMode.Custom;
            tip.CustomPopupPlacementCallback=(popup,target,offset)=>new[]{new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point((target.Width-popup.Width)/2,-popup.Height-8),System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)};
        }));
        MainWindow = new GlassDock.MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if(MainWindow is GlassDock.MainWindow dock)dock.SaveStateOnExit();
        exiting = true;
        settingsRegistration?.Unregister(null);
        settingsRegistration = null;
        settingsRequest?.Dispose();
        settingsRequest = null;
        if (ownsInstance)
        {
            try { instance?.ReleaseMutex(); }
            catch (ApplicationException) { /* Startup may have ended before ownership settled. */ }
        }
        instance?.Dispose();
        instance = null;
        if (e.ApplicationExitCode == 0) cleanExit?.Set();
        cleanExit?.Dispose();
        base.OnExit(e);
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if(MainWindow is GlassDock.MainWindow dock)dock.SaveStateOnExit();
        base.OnSessionEnding(e);
        if (!e.Cancel) cleanExit?.Set();
    }
}
