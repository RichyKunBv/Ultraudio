using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ultraudio;

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
}
