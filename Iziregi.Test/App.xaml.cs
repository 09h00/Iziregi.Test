using System.Windows;
using Iziregi.Test.Services;

namespace Iziregi.Test
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            PdfService.Configure();
        }
    }
}