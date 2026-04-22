// File: ListWindow.xaml.cs
using System.Windows;
using Iziregi.Test.Pages;

namespace Iziregi.Test;

public partial class ListWindow : Window
{
    private readonly MainWindow _host;

    public ListWindow(MainWindow host)
    {
        InitializeComponent();

        _host = host;
        HostContent.Content = new ListsPage(_host);
    }
}