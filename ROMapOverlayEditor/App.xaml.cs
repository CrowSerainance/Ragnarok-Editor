using System.Windows;

namespace ROMapOverlayEditor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // MessageBox.Show("App Starting..."); // Debug
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            System.IO.File.WriteAllText("crash_domain.log", args.ExceptionObject.ToString());
            MessageBox.Show("Domain Crash! See crash_domain.log");
        };

        base.OnStartup(e);
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        System.IO.File.WriteAllText("crash_dispatcher.log", e.Exception.ToString());
        MessageBox.Show("Dispatcher Crash! See crash_dispatcher.log");
        e.Handled = true;
        Shutdown();
    }
}
