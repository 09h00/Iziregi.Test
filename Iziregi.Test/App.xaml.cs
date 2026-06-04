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
        }
    }
}