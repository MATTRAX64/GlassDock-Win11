using System.Windows;
using System.Threading;
using System.Windows.Threading;
namespace GlassDock;
public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? settingsRequest;
    private RegisteredWaitHandle? settingsRegistration;
    private bool ownsInstance;
    private volatile bool exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Create/open the signal first: a second launch during initial startup can set
        // it before the first instance registers its listener without losing the request.
        settingsRequest = new EventWaitHandle(false, EventResetMode.AutoReset,
            "Local\\GlassDock.ShowSettings");
        instance = new Mutex(true, "Local\\GlassDock.Main", out var created);
        ownsInstance = created;
        if (!created)
        {
            settingsRequest.Set();
            Shutdown();
            return;
        }

        settingsRegistration = ThreadPool.RegisterWaitForSingleObject(settingsRequest,
            (_, _) =>
            {
                if (exiting || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
                // WPF controls remain on their dispatcher. ApplicationIdle also lets
                // StartupUri create and load MainWindow before an early wake request.
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
        base.OnExit(e);
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if(MainWindow is GlassDock.MainWindow dock)dock.SaveStateOnExit();
        base.OnSessionEnding(e);
    }
}
