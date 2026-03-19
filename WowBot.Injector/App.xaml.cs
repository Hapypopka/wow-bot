using System.Windows;
using System.Windows.Threading;

namespace WowBot.Injector;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WowBot.Core.Logger.Error($"UNHANDLED UI EXCEPTION: {e.Exception.Message}\n{e.Exception.StackTrace}");
        e.Handled = true; // Не крашим приложение
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        WowBot.Core.Logger.Error($"UNHANDLED EXCEPTION: {ex?.Message}\n{ex?.StackTrace}");
    }
}
