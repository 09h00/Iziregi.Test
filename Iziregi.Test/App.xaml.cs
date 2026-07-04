// File: App.xaml.cs
using System.Windows;
using Iziregi.Test.Services;

namespace Iziregi.Test
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            PdfService.Configure();
            // Global exception handlers for diagnostics
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            // Capture les exceptions asynchrones non observées
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("DispatcherUnhandledException: " + e.Exception);
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Iziregi_unhandled_exception.txt");
                System.IO.File.WriteAllText(path, e.Exception.ToString());
                // Empêche l'application de se fermer automatiquement pour les exceptions non gérées côté UI
                e.Handled = true;
            }
            catch { }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                System.Diagnostics.Debug.WriteLine("CurrentDomain_UnhandledException: " + ex);
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Iziregi_unhandled_exception.txt");
                System.IO.File.WriteAllText(path, ex?.ToString() ?? "(null)");
            }
            catch { }
        }

        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                var ag = e.Exception;
                System.Diagnostics.Debug.WriteLine("UnobservedTaskException: " + ag);
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Iziregi_unhandled_exception.txt");
                System.IO.File.WriteAllText(path, ag.ToString());
                e.SetObserved();
            }
            catch { }
        }
    }
}