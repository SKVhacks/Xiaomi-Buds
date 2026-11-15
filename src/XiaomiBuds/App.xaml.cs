using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using XiaomiBuds.Core;

namespace XiaomiBuds;

public partial class App : Application
{
    const string InstanceMutex = @"Local\XiaomiBuds.Instance";
    const string ShowEvent = @"Local\XiaomiBuds.Show";

    public static MainWindow? Main { get; private set; }
    Mutex? _mutex;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => { Log.Error("unhandled UI exception", e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Warn("unobserved task: " + e.Exception.Message); e.SetObserved(); };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Single instance: the buds accept one control connection, so a second copy just
        // asks the first one to come to the front and exits.
        _mutex = new Mutex(true, InstanceMutex, out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEvent).Set(); } catch { }
            Exit();
            return;
        }

        Log.Info("Xiaomi Buds starting");
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEvent);
        Main = new MainWindow();
        var dq = DispatcherQueue.GetForCurrentThread();
        new Thread(() =>
        {
            while (show.WaitOne()) dq.TryEnqueue(() => Main.ShowAndActivate());
        }) { IsBackground = true }.Start();

        var argv = Environment.GetCommandLineArgs();
        if (!argv.Contains("--tray"))
            Main.ShowAndActivate();
    }
}
