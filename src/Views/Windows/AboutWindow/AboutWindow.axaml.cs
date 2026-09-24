using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ultraudio.Views.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        TxtVersion.Text = "Versión " + AppInfo.VersionDisplay;
    }

    private void BtnCerrar_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void BtnLogs_Click(object? sender, RoutedEventArgs e)
    {
        var logsWin = new LogsWindow();
        await logsWin.ShowDialog(this);
    }
}
