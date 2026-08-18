// File: IdentityWindow.xaml.cs
using System.Windows;
using Iziregi.Test.Pages;

namespace Iziregi.Test;

public partial class IdentityWindow : Window
{
    public IdentityWindow(MainWindow host, string serverBaseUrl, string serverApiKey)
    {
        InitializeComponent();

        var page = new ArchitectIdentityPage(host, serverBaseUrl, serverApiKey);
        page.CloseRequested += (_, __) => Close();
        Host.Content = page;
    }
}
