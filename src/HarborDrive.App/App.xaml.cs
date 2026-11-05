using System.IO;
using System.Security.Principal;
using System.Windows;
using HarborDrive.Core;

namespace HarborDrive.App;

public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    private EventWaitHandle? activate;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smokeTest = e.Args.Contains("--smoke-test");
        var name = "Local\\HarborDrive-" + WindowsIdentity.GetCurrent().User!.Value;
        mutex = new Mutex(true, name, out var first);
        activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-show");
        if (!first) { if (!smokeTest) activate.Set(); Shutdown(smokeTest ? 2 : 0); return; }
        DispatcherUnhandledException += (_, args) => { System.Windows.MessageBox.Show(args.Exception.Message, "HarborDrive", MessageBoxButton.OK, MessageBoxImage.Error); args.Handled = true; };
        try
        {
            var store = new AppStore();
            var window = new MainWindow(store, smokeTest ? new AppSettings { CheckForUpdates = false } : store.Load());
            MainWindow = window;
            if (!smokeTest && !e.Args.Contains("--background")) window.Show();
            if (smokeTest) Dispatcher.BeginInvoke(async () => await window.FinishSmokeTest());
            ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => Dispatcher.BeginInvoke(() => window.ShowDashboard()), null, -1, false);
        }
        catch (Exception ex) { if (!smokeTest) System.Windows.MessageBox.Show("앱을 시작하지 못했습니다. 설정과 캐시는 보존됩니다.\n\n" + ex.Message, "HarborDrive", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { activate?.Dispose(); mutex?.Dispose(); base.OnExit(e); }
}
